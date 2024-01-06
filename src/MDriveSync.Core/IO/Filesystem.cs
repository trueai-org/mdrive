namespace MDriveSync.Core.IO
{
    public class Filesystem
    {
        //public void GET(string key, RequestInfo info)
        //{
        //    var parts = (key ?? "").Split(new char[] { '/' });
        //    var path = Duplicati.Library.Utility.Uri.UrlDecode((parts.Length == 2 ? parts.FirstOrDefault() : key ?? ""));
        //    var command = parts.Length == 2 ? parts.Last() : null;
        //    if (string.IsNullOrEmpty(path))
        //        path = info.Request.QueryString["path"].Value;

        //    Process(command, path, info);
        //}

        public void Process(string command, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                //info.ReportClientError("No path parameter was found", System.Net.HttpStatusCode.BadRequest);
                return;
            }

            bool skipFiles = true; // Library.Utility.Utility.ParseBool(info.Request.QueryString["onlyfolders"].Value, false);
            bool showHidden = true; // Library.Utility.Utility.ParseBool(info.Request.QueryString["showhidden"].Value, false);

            string specialpath = null;
            string specialtoken = null;

            if (path.StartsWith("%", StringComparison.Ordinal))
            {
                var ix = path.IndexOf("%", 1, StringComparison.Ordinal);
                if (ix > 0)
                {
                    var tk = path.Substring(0, ix + 1);
                    var node = SpecialFolders.Nodes.FirstOrDefault(x => x.id.Equals(tk, StringComparison.OrdinalIgnoreCase));
                    if (node != null)
                    {
                        specialpath = node.resolvedpath;
                        specialtoken = node.id;
                    }
                }
            }

            path = SpecialFolders.ExpandEnvironmentVariables(path);

            if (Platform.IsClientPosix && !path.StartsWith("/", StringComparison.Ordinal))
            {
                //info.ReportClientError("The path parameter must start with a forward-slash", System.Net.HttpStatusCode.BadRequest);
                return;
            }

            //if (!string.IsNullOrWhiteSpace(command))
            //{
            //    if ("validate".Equals(command, StringComparison.OrdinalIgnoreCase))
            //    {
            //        try
            //        {
            //            if (System.IO.Path.IsPathRooted(path) && (System.IO.Directory.Exists(path) || System.IO.File.Exists(path)))
            //            {
            //                //info.OutputOK();
            //                return;
            //            }
            //        }
            //        catch
            //        {
            //        }

            //        //info.ReportServerError("File or folder not found", System.Net.HttpStatusCode.NotFound);
            //        return;
            //    }
            //    else
            //    {
            //        //info.ReportClientError(string.Format("No such operation found: {0}", command), System.Net.HttpStatusCode.NotFound);
            //        return;
            //    }
            //}

            try
            {
                if (path != "" && path != "/")
                    path = Util.AppendDirSeparator(path);

                IEnumerable<TreeNode> res;

                if (!Platform.IsClientPosix && (path.Equals("/") || path.Equals("")))
                {
                    res = DriveInfo.GetDrives()
                            .Where(di =>
                                (di.DriveType == DriveType.Fixed || di.DriveType == DriveType.Network || di.DriveType == DriveType.Removable)
                                && di.IsReady // Only try to create TreeNode entries for drives who were ready 'now'
                            )
                            .Select(TryCreateTreeNodeForDrive) // This will try to create a TreeNode for selected drives
                            .Where(tn => tn != null); // This filters out such entries that could not be created
                }
                else
                {
                    res = ListFolderAsNodes(path, skipFiles, showHidden);
                }

                if ((path.Equals("/") || path.Equals("")) && specialtoken == null)
                {
                    // Prepend special folders
                    res = SpecialFolders.Nodes.Union(res);
                }

                if (specialtoken != null)
                {
                    res = res.Select(x =>
                    {
                        x.resolvedpath = x.id;
                        x.id = specialtoken + x.id.Substring(specialpath.Length);
                        return x;
                    });
                }

                // We have to resolve the query before giving it to OutputOK
                // If we do not do this, and the query throws an exception when OutputOK resolves it,
                // the exception would not be handled properly
                res = res.ToList();

                //info.OutputOK(res);
            }
            catch (Exception ex)
            {
                //info.ReportClientError("Failed to process the path: " + ex.Message, System.Net.HttpStatusCode.InternalServerError);
            }
        }

        /// <summary>
        /// Try to create a new TreeNode instance for the given DriveInfo instance.
        ///
        /// <remarks>
        /// If an exception occurs during creation (most likely the device became unavailable), a null is returned instead.
        /// </remarks>
        /// </summary>
        /// <param name="driveInfo">DriveInfo to try create a TreeNode for. Cannot be null.</param>
        /// <returns>A new TreeNode instance on success; null if an exception occurred during creation.</returns>
        private static TreeNode TryCreateTreeNodeForDrive(DriveInfo driveInfo)
        {
            if (driveInfo == null) throw new ArgumentNullException(nameof(driveInfo));

            try
            {
                // Try to create the TreeNode
                // This may still fail as the drive might become unavailable in the meanwhile
                return new TreeNode
                {
                    id = driveInfo.RootDirectory.FullName,
                    text =
                    (
                        string.IsNullOrWhiteSpace(driveInfo.VolumeLabel)
                            ? driveInfo.RootDirectory.FullName.Replace('\\', ' ')
                            : driveInfo.VolumeLabel + " - " + driveInfo.RootDirectory.FullName.Replace('\\', ' ')
                        ) + "(" + driveInfo.DriveType + ")",
                    iconCls = "x-tree-icon-drive"
                };
            }
            catch
            {
                // Drive became unavailable in the meanwhile or another exception occurred
                // Return a null as fall back
                return null;
            }
        }

        private static IEnumerable<TreeNode> ListFolderAsNodes(string entrypath, bool skipFiles, bool showHidden)
        {
            //Helper function for finding out if a folder has sub elements
            Func<string, bool> hasSubElements = (p) => skipFiles ? Directory.EnumerateDirectories(p).Any() : Directory.EnumerateFileSystemEntries(p).Any();

            //Helper function for dealing with exceptions when accessing off-limits folders
            Func<string, bool> isEmptyFolder = (p) =>
            {
                try { return !hasSubElements(p); }
                catch { }
                return true;
            };

            //Helper function for dealing with exceptions when accessing off-limits folders
            Func<string, bool> canAccess = (p) =>
            {
                try { hasSubElements(p); return true; }
                catch { }
                return false;
            };

            foreach (var s in SystemIO.IO_OS.EnumerateFileSystemEntries(entrypath)
                // Group directories first
                .OrderByDescending(f => SystemIO.IO_OS.GetFileAttributes(f) & FileAttributes.Directory)
            // Sort both groups (directories and files) alphabetically
                .ThenBy(f => f))
            {
                TreeNode tn = null;
                try
                {
                    var attr = SystemIO.IO_OS.GetFileAttributes(s);
                    var isSymlink = SystemIO.IO_OS.IsSymlink(s, attr);
                    var isFolder = (attr & FileAttributes.Directory) != 0;
                    var isFile = !isFolder;
                    var isHidden = (attr & FileAttributes.Hidden) != 0;

                    var accessible = isFile || canAccess(s);
                    var isLeaf = isFile || !accessible || isEmptyFolder(s);

                    var rawid = isFolder ? Util.AppendDirSeparator(s) : s;
                    if (skipFiles && !isFolder)
                        continue;

                    if (!showHidden && isHidden)
                        continue;

                    tn = new TreeNode()
                    {
                        id = rawid,
                        text = SystemIO.IO_OS.PathGetFileName(s),
                        hidden = isHidden,
                        symlink = isSymlink,
                        iconCls = isFolder ? (accessible ? (isSymlink ? "x-tree-icon-symlink" : "x-tree-icon-parent") : "x-tree-icon-locked") : "x-tree-icon-leaf",
                        leaf = isLeaf
                    };
                }
                catch
                {
                }

                if (tn != null)
                    yield return tn;
            }
        }

        //public void POST(string key, RequestInfo info)
        //{
        //    Process(key, info.Request.Form["path"].Value, info);
        //}

        //public string Description { get { return "Enumerates the server filesystem"; } }

        //public IEnumerable<KeyValuePair<string, Type>> Types
        //{
        //    get
        //    {
        //        return new KeyValuePair<string, Type>[] {
        //            new KeyValuePair<string, Type>(HttpServer.Method.Get, typeof(string[])),
        //        };
        //    }
        //}
    }
}