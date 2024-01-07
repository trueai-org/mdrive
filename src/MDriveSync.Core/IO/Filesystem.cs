namespace MDriveSync.Core.IO
{
    /// <summary>
    /// 文件系统
    /// </summary>
    public class Filesystem
    {
        /// <summary>
        /// 获取文件列表
        /// </summary>
        /// <param name="path"></param>
        /// <param name="command"></param>
        /// <param name="skipFiles"></param>
        /// <param name="showHidden"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        /// <exception cref="Exception"></exception>
        public static List<TreeNode> TreeNodes(string path = "", string command = "", bool skipFiles = true, bool showHidden = true)
        {
            //if (string.IsNullOrEmpty(path))
            //{
            //    throw new LogicException("No path parameter was found");
            //}

            var list = new List<TreeNode>();

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

            // 如果是 linux
            if (Platform.IsClientPosix)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    path = "/";
                }

                if (!path.StartsWith("/", StringComparison.Ordinal))
                {
                    throw new LogicException("The path parameter must start with a forward-slash");
                }
            }

            if (!string.IsNullOrWhiteSpace(command))
            {
                if ("validate".Equals(command, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (Path.IsPathRooted(path) && (Directory.Exists(path) || File.Exists(path)))
                        {
                            return list;
                        }
                    }
                    catch
                    {
                    }

                    throw new LogicException("File or folder not found");
                }
                else
                {
                    throw new LogicException(string.Format("No such operation found: {0}", command));
                }
            }

            try
            {
                if (path != "" && path != "/")
                    path = Util.AppendDirSeparator(path);

                IEnumerable<TreeNode> res;

                if (!Platform.IsClientPosix && (path.Equals("/") || path.Equals("")))
                {
                    res = DriveInfo.GetDrives()
                            .Where(di => (di.DriveType == DriveType.Fixed || di.DriveType == DriveType.Network || di.DriveType == DriveType.Removable)
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

                return res.ToList();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to process the path: {path}", ex);
            }
        }

        /// <summary>
        /// 尝试为给定的DriveInfo实例创建一个新的TreeNode实例。
        ///
        /// <remarks>
        /// 如果在创建过程中发生异常（最可能的原因是设备变得不可用），则返回null。
        /// </remarks>
        /// </summary>
        /// <param name="driveInfo">尝试为其创建TreeNode的DriveInfo。不能为null。</param>
        /// <returns>成功时返回一个新的TreeNode实例；如果在创建过程中发生异常，则返回null。</returns>
        private static TreeNode TryCreateTreeNodeForDrive(DriveInfo driveInfo)
        {
            if (driveInfo == null)
                throw new ArgumentNullException(nameof(driveInfo));  // 如果driveInfo为null，则抛出异常

            try
            {
                // 尝试创建TreeNode
                // 这可能会失败，因为驱动器可能在此期间变得不可用
                return new TreeNode
                {
                    id = driveInfo.RootDirectory.FullName,
                    text =
                    (
                        string.IsNullOrWhiteSpace(driveInfo.VolumeLabel)
                            ? driveInfo.RootDirectory.FullName.Replace('\\', ' ')
                            : driveInfo.VolumeLabel + " - " + driveInfo.RootDirectory.FullName.Replace('\\', ' ')
                        ) + "(" + driveInfo.DriveType + ")",
                    iconCls = "x-tree-icon-drive"  // 设置TreeNode的图标类
                };
            }
            catch
            {
                // 驱动器在此期间变得不可用或发生其他异常
                // 返回null作为备选方案
                return null;
            }
        }

        private static IEnumerable<TreeNode> ListFolderAsNodes(string entrypath, bool skipFiles, bool showHidden)
        {
            // 用于确定文件夹是否具有子元素的帮助函数
            Func<string, bool> hasSubElements = (p) => skipFiles ? Directory.EnumerateDirectories(p).Any() : Directory.EnumerateFileSystemEntries(p).Any();

            // 用于处理在访问受限文件夹时出现的异常的帮助函数
            Func<string, bool> isEmptyFolder = (p) =>
            {
                try { return !hasSubElements(p); }
                catch { }
                return true;
            };

            // 用于处理在访问受限文件夹时出现的异常的帮助函数
            Func<string, bool> canAccess = (p) =>
            {
                try { hasSubElements(p); return true; }
                catch { }
                return false;
            };

            foreach (var s in SystemIO.IO_OS.EnumerateFileSystemEntries(entrypath)

                // 首先按目录分组
                .OrderByDescending(f => SystemIO.IO_OS.GetFileAttributes(f) & FileAttributes.Directory)

                // 对两组（目录和文件）按字母顺序排序
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
                        leaf = isLeaf  // 设置是否为叶子节点
                    };
                }
                catch
                {
                    // 忽略异常，继续处理下一个文件系统条目
                }

                if (tn != null)
                    yield return tn;  // 如果TreeNode不为null，则返回它
            }
        }
    }
}