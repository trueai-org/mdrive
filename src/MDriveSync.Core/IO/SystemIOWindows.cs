using Newtonsoft.Json;
using System.Security;
using System.Security.AccessControl;

namespace MDriveSync.Core.IO
{
    /// <summary>
    /// 提供Windows系统IO操作的结构体。
    /// </summary>
    public struct SystemIOWindows : ISystemIO
    {
        // 基于以下网址中的常量名称：
        // https://github.com/dotnet/runtime/blob/v5.0.12/src/libraries/Common/src/System/IO/PathInternal.Windows.cs
        private const string ExtendedDevicePathPrefix = @"\\?\";

        private const string UncPathPrefix = @"\\";
        private const string AltUncPathPrefix = @"//";
        private const string UncExtendedPathPrefix = @"\\?\UNC\";

        private static readonly string DIRSEP = Util.DirectorySeparatorString;

        /// <summary>
        /// 为路径添加扩展设备路径前缀（@"\\?\" 或 @"\\?\UNC\"），但仅当它是一个完全限定的
        /// 路径且没有相对组件（即路径中没有"." 或 ".."）时。
        /// </summary>
        public static string AddExtendedDevicePathPrefix(string path)
        {
            if (IsPrefixedWithExtendedDevicePathPrefix(path))
            {
                // 例如：\\?\C:\Temp\foo.txt 或 \\?\UNC\example.com\share\foo.txt
                return path;
            }
            else
            {
                var hasRelativePathComponents = HasRelativePathComponents(path);
                if (IsPrefixedWithUncPathPrefix(path) && !hasRelativePathComponents)
                {
                    // 例如：\\example.com\share\foo.txt 或 //example.com/share/foo.txt
                    return UncExtendedPathPrefix + ConvertSlashes(path.Substring(UncPathPrefix.Length));
                }
                else if (DotNetRuntimePathWindows.IsPathFullyQualified(path) && !hasRelativePathComponents)
                {
                    // 例如：C:\Temp\foo.txt 或 C:/Temp/foo.txt
                    return ExtendedDevicePathPrefix + ConvertSlashes(path);
                }
                else
                {
                    // 相对路径或带有相对路径组件的完全限定路径，因此不能应用扩展设备路径前缀。
                    // 例如：foo.txt 或 C:\Temp\..\foo.txt
                    return path;
                }
            }
        }

        /// <summary>
        /// 如果路径以 @"\\" 或 @"//" 开头，则返回 true。
        /// </summary>
        private static bool IsPrefixedWithUncPathPrefix(string path)
        {
            return path.StartsWith(UncPathPrefix, StringComparison.Ordinal) ||
                path.StartsWith(AltUncPathPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// 如果路径以 @"\\?\UNC\" 或 @"\\?\" 开头，则返回 true。
        /// </summary>
        private static bool IsPrefixedWithExtendedDevicePathPrefix(string path)
        {
            return path.StartsWith(UncExtendedPathPrefix, StringComparison.Ordinal) ||
                path.StartsWith(ExtendedDevicePathPrefix, StringComparison.Ordinal);
        }

        private static string[] relativePathComponents = new[] { ".", ".." };

        /// <summary>
        /// 如果 <paramref name="path"/> 包含相对路径组件（"." 或 ".."），则返回 true。
        /// </summary>
        private static bool HasRelativePathComponents(string path)
        {
            return GetPathComponents(path).Any(pathComponent => relativePathComponents.Contains(pathComponent));
        }

        /// <summary>
        /// 返回代表 <paramref name="path"/> 中的文件和目录的序列。
        /// </summary>
        private static IEnumerable<string> GetPathComponents(string path)
        {
            while (!String.IsNullOrEmpty(path))
            {
                var pathComponent = Path.GetFileName(path);
                if (!String.IsNullOrEmpty(pathComponent))
                {
                    yield return pathComponent;
                }
                path = Path.GetDirectoryName(path);
            }
        }

        /// <summary>
        /// 如果 <paramref name="path"/> 以扩展设备路径前缀（@"\\?\" 或 @"\\?\UNC\"）开头，
        /// 则移除它。
        /// </summary>
        public static string RemoveExtendedDevicePathPrefix(string path)
        {
            if (path.StartsWith(UncExtendedPathPrefix, StringComparison.Ordinal))
            {
                // @"\\?\UNC\example.com\share\file.txt" 变为 @"\\example.com\share\file.txt"
                return UncPathPrefix + path.Substring(UncExtendedPathPrefix.Length);
            }
            else if (path.StartsWith(ExtendedDevicePathPrefix, StringComparison.Ordinal))
            {
                // @"\\?\C:\file.txt" 变为 @"C:\file.txt"
                return path.Substring(ExtendedDevicePathPrefix.Length);
            }
            else
            {
                return path;
            }
        }

        /// <summary>
        /// 将路径中的正斜杠转换为反斜杠。
        /// </summary>
        /// <returns>替换了正斜杠的路径。</returns>
        private static string ConvertSlashes(string path)
        {
            return path.Replace("/", Util.DirectorySeparatorString);
        }

        private class FileSystemAccess
        {
            // 使用JsonProperty属性允许反序列化器设置只读字段
            // 参见：https://github.com/duplicati/duplicati/issues/4028
            [JsonProperty]
            public readonly FileSystemRights Rights;

            [JsonProperty]
            public readonly AccessControlType ControlType;

            [JsonProperty]
            public readonly string SID;

            [JsonProperty]
            public readonly bool Inherited;

            [JsonProperty]
            public readonly InheritanceFlags Inheritance;

            [JsonProperty]
            public readonly PropagationFlags Propagation;

            public FileSystemAccess()
            {
            }

            public FileSystemAccess(FileSystemAccessRule rule)
            {
                Rights = rule.FileSystemRights;
                ControlType = rule.AccessControlType;
                SID = rule.IdentityReference.Value;
                Inherited = rule.IsInherited;
                Inheritance = rule.InheritanceFlags;
                Propagation = rule.PropagationFlags;
            }

            public FileSystemAccessRule Create(System.Security.AccessControl.FileSystemSecurity owner)
            {
                return (FileSystemAccessRule)owner.AccessRuleFactory(
                    new System.Security.Principal.SecurityIdentifier(SID),
                    (int)Rights,
                    Inherited,
                    Inheritance,
                    Propagation,
                    ControlType);
            }
        }

        private static Newtonsoft.Json.JsonSerializer _cachedSerializer;

        private Newtonsoft.Json.JsonSerializer Serializer
        {
            get
            {
                if (_cachedSerializer != null)
                {
                    return _cachedSerializer;
                }

                _cachedSerializer = Newtonsoft.Json.JsonSerializer.Create(
                    new Newtonsoft.Json.JsonSerializerSettings { Culture = System.Globalization.CultureInfo.InvariantCulture });

                return _cachedSerializer;
            }
        }

        private string SerializeObject<T>(T o)
        {
            using (var tw = new System.IO.StringWriter())
            {
                Serializer.Serialize(tw, o);
                tw.Flush();
                return tw.ToString();
            }
        }

        private T DeserializeObject<T>(string data)
        {
            using (var tr = new System.IO.StringReader(data))
            {
                return (T)Serializer.Deserialize(tr, typeof(T));
            }
        }

        private System.Security.AccessControl.FileSystemSecurity GetAccessControlDir(string path)
        {
            return DirectoryGetAccessControl(AddExtendedDevicePathPrefix(path));
        }

        private System.Security.AccessControl.FileSystemSecurity GetAccessControlFile(string path)
        {
            return FileGetAccessControl(AddExtendedDevicePathPrefix(path));
        }

        private void SetAccessControlFile(string path, FileSecurity rules)
        {
            FileSetAccessControl(AddExtendedDevicePathPrefix(path), rules);
        }

        private void SetAccessControlDir(string path, DirectorySecurity rules)
        {
            DirectorySetAccessControl(AddExtendedDevicePathPrefix(path), rules);
        }

        /// <summary>
        /// 设置目录的访问控制和安全策略。
        /// </summary>
        /// <param name="path">目录的路径。</param>
        /// <param name="directorySecurity">目录的安全和访问策略。</param>
        [SecuritySafeCritical]
        public static void DirectorySetAccessControl(string path, DirectorySecurity directorySecurity)
        {
            if (directorySecurity == null)
            {
                throw new ArgumentNullException(nameof(directorySecurity));
            }

            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("路径不能为空", nameof(path));
            }

            string fullPath = Path.GetFullPath(path);

            try
            {
                var directoryInfo = new DirectoryInfo(fullPath);
                directoryInfo.SetAccessControl(directorySecurity);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("无法设置目录的访问控制。", ex);
            }
        }

        public static DirectorySecurity DirectoryGetAccessControl(string path)
        {
            return new DirectorySecurity(path, AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group);
        }

        public static DirectorySecurity DirectoryGetAccessControl(string path, AccessControlSections includeSections)
        {
            return new DirectorySecurity(path, includeSections);
        }

        public static FileSecurity FileGetAccessControl(string path)
        {
            return FileGetAccessControl(path, AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group);
        }

        public static FileSecurity FileGetAccessControl(string path, AccessControlSections includeSections)
        {
            return new FileSecurity(path, includeSections);
        }

        /// <summary>
        /// 设置文件的访问控制和安全策略。
        /// </summary>
        /// <param name="path">文件的路径。</param>
        /// <param name="fileSecurity">文件的安全和访问策略。</param>
        [SecuritySafeCritical]
        public static void FileSetAccessControl(string path, FileSecurity fileSecurity)
        {
            if (fileSecurity == null)
            {
                throw new ArgumentNullException(nameof(fileSecurity));
            }

            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("路径不能为空", nameof(path));
            }

            string fullPath = Path.GetFullPath(path);

            try
            {
                var fileInfo = new FileInfo(fullPath);
                fileInfo.SetAccessControl(fileSecurity);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("无法设置文件的访问控制。", ex);
            }
        }

        #region ISystemIO implementation

        public void DirectoryCreate(string path)
        {
            System.IO.Directory.CreateDirectory(AddExtendedDevicePathPrefix(path));
        }

        public void DirectoryDelete(string path, bool recursive)
        {
            System.IO.Directory.Delete(AddExtendedDevicePathPrefix(path), recursive);
        }

        public bool DirectoryExists(string path)
        {
            return System.IO.Directory.Exists(AddExtendedDevicePathPrefix(path));
        }

        public void DirectoryMove(string sourceDirName, string destDirName)
        {
            System.IO.Directory.Move(AddExtendedDevicePathPrefix(sourceDirName), AddExtendedDevicePathPrefix(destDirName));
        }

        public void FileDelete(string path)
        {
            System.IO.File.Delete(AddExtendedDevicePathPrefix(path));
        }

        public void FileSetLastWriteTimeUtc(string path, DateTime time)
        {
            System.IO.File.SetLastWriteTimeUtc(AddExtendedDevicePathPrefix(path), time);
        }

        public void FileSetCreationTimeUtc(string path, DateTime time)
        {
            System.IO.File.SetCreationTimeUtc(AddExtendedDevicePathPrefix(path), time);
        }

        public DateTime FileGetLastWriteTimeUtc(string path)
        {
            return System.IO.File.GetLastWriteTimeUtc(AddExtendedDevicePathPrefix(path));
        }

        public DateTime FileGetCreationTimeUtc(string path)
        {
            return System.IO.File.GetCreationTimeUtc(AddExtendedDevicePathPrefix(path));
        }

        public bool FileExists(string path)
        {
            return System.IO.File.Exists(AddExtendedDevicePathPrefix(path));
        }

        public System.IO.FileStream FileOpenRead(string path)
        {
            return System.IO.File.Open(AddExtendedDevicePathPrefix(path), System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
        }

        public System.IO.FileStream FileOpenWrite(string path)
        {
            return !FileExists(path)
                ? FileCreate(path)
                : System.IO.File.OpenWrite(AddExtendedDevicePathPrefix(path));
        }

        public System.IO.FileStream FileCreate(string path)
        {
            return System.IO.File.Create(AddExtendedDevicePathPrefix(path));
        }

        public System.IO.FileAttributes GetFileAttributes(string path)
        {
            return System.IO.File.GetAttributes(AddExtendedDevicePathPrefix(path));
        }

        public void SetFileAttributes(string path, System.IO.FileAttributes attributes)
        {
            System.IO.File.SetAttributes(AddExtendedDevicePathPrefix(path), attributes);
        }

        /// <summary>
        /// 如果条目是符号链接，则返回符号链接的目标，否则返回null
        /// </summary>
        /// <param name="file">要检查的文件或文件夹</param>
        /// <returns>符号链接的目标</returns>
        public string GetSymlinkTarget(string file)
        {
            // 检查提供的路径是否存在
            if (!File.Exists(file) && !Directory.Exists(file))
            {
                return null;
            }

            // 获取文件或目录的信息
            var fileInfo = new FileInfo(file);

            if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                // 处理文件符号链接
                if (fileInfo.Attributes.HasFlag(FileAttributes.Directory))
                {
                    // 对于目录，使用DirectoryInfo
                    var dirInfo = new DirectoryInfo(file);
                    return dirInfo.LinkTarget;
                }
                else
                {
                    // 对于文件，直接使用FileInfo
                    return fileInfo.LinkTarget;
                }
            }

            // 如果不是符号链接，则返回null
            return null;
        }

        public IEnumerable<string> EnumerateFileSystemEntries(string path)
        {
            return System.IO.Directory.EnumerateFileSystemEntries(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix);
        }

        public IEnumerable<string> EnumerateFiles(string path)
        {
            return System.IO.Directory.EnumerateFiles(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return System.IO.Directory.EnumerateFiles(AddExtendedDevicePathPrefix(path), searchPattern, searchOption).Select(RemoveExtendedDevicePathPrefix);
        }

        public string PathGetFileName(string path)
        {
            return RemoveExtendedDevicePathPrefix(System.IO.Path.GetFileName(AddExtendedDevicePathPrefix(path)));
        }

        public string PathGetDirectoryName(string path)
        {
            return RemoveExtendedDevicePathPrefix(System.IO.Path.GetDirectoryName(AddExtendedDevicePathPrefix(path)));
        }

        public string PathGetExtension(string path)
        {
            return RemoveExtendedDevicePathPrefix(System.IO.Path.GetExtension(AddExtendedDevicePathPrefix(path)));
        }

        public string PathChangeExtension(string path, string extension)
        {
            return RemoveExtendedDevicePathPrefix(System.IO.Path.ChangeExtension(AddExtendedDevicePathPrefix(path), extension));
        }

        public void DirectorySetLastWriteTimeUtc(string path, DateTime time)
        {
            System.IO.Directory.SetLastWriteTimeUtc(AddExtendedDevicePathPrefix(path), time);
        }

        public void DirectorySetCreationTimeUtc(string path, DateTime time)
        {
            System.IO.Directory.SetCreationTimeUtc(AddExtendedDevicePathPrefix(path), time);
        }

        public void FileMove(string source, string target)
        {
            System.IO.File.Move(AddExtendedDevicePathPrefix(source), AddExtendedDevicePathPrefix(target));
        }

        public long FileLength(string path)
        {
            return new System.IO.FileInfo(AddExtendedDevicePathPrefix(path)).Length;
        }

        public string GetPathRoot(string path)
        {
            if (IsPrefixedWithExtendedDevicePathPrefix(path))
            {
                return Path.GetPathRoot(path);
            }
            else
            {
                return RemoveExtendedDevicePathPrefix(Path.GetPathRoot(AddExtendedDevicePathPrefix(path)));
            }
        }

        public string[] GetDirectories(string path)
        {
            if (IsPrefixedWithExtendedDevicePathPrefix(path))
            {
                return Directory.GetDirectories(path);
            }
            else
            {
                return Directory.GetDirectories(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix).ToArray();
            }
        }

        public string[] GetFiles(string path)
        {
            if (IsPrefixedWithExtendedDevicePathPrefix(path))
            {
                return Directory.GetFiles(path);
            }
            else
            {
                return Directory.GetFiles(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix).ToArray();
            }
        }

        public string[] GetFiles(string path, string searchPattern)
        {
            if (IsPrefixedWithExtendedDevicePathPrefix(path))
            {
                return Directory.GetFiles(path, searchPattern);
            }
            else
            {
                return Directory.GetFiles(AddExtendedDevicePathPrefix(path), searchPattern).Select(RemoveExtendedDevicePathPrefix).ToArray();
            }
        }

        public DateTime GetCreationTimeUtc(string path)
        {
            return Directory.GetCreationTimeUtc(AddExtendedDevicePathPrefix(path));
        }

        public DateTime GetLastWriteTimeUtc(string path)
        {
            return Directory.GetLastWriteTimeUtc(AddExtendedDevicePathPrefix(path));
        }

        public IEnumerable<string> EnumerateDirectories(string path)
        {
            if (IsPrefixedWithExtendedDevicePathPrefix(path))
            {
                return Directory.EnumerateDirectories(path);
            }
            else
            {
                return Directory.EnumerateDirectories(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix);
            }
        }

        public void FileCopy(string source, string target, bool overwrite)
        {
            File.Copy(AddExtendedDevicePathPrefix(source), AddExtendedDevicePathPrefix(target), overwrite);
        }

        public string PathGetFullPath(string path)
        {
            // Desired behavior:
            // 1. If path is already prefixed with \\?\, it should be left untouched
            // 2. If path is not already prefixed with \\?\, the return value should also not be prefixed
            // 3. If path is relative or has relative components, that should be resolved by calling Path.GetFullPath()
            // 4. If path is not relative and has no relative components, prefix with \\?\ to prevent normalization from munging "problematic Windows paths"
            if (IsPrefixedWithExtendedDevicePathPrefix(path))
            {
                return path;
            }
            else
            {
                return RemoveExtendedDevicePathPrefix(Path.GetFullPath(AddExtendedDevicePathPrefix(path)));
            }
        }

        public IFileEntry DirectoryEntry(string path)
        {
            var dInfo = new DirectoryInfo(AddExtendedDevicePathPrefix(path));
            return new FileEntry(dInfo.Name, 0, dInfo.LastAccessTime, dInfo.LastWriteTime)
            {
                IsFolder = true
            };
        }

        public IFileEntry FileEntry(string path)
        {
            var fileInfo = new FileInfo(AddExtendedDevicePathPrefix(path));
            var lastAccess = new DateTime();
            try
            {
                // Internally this will convert the FILETIME value from Windows API to a
                // DateTime. If the value represents a date after 12/31/9999 it will throw
                // ArgumentOutOfRangeException, because this is not supported by DateTime.
                // Some file systems seem to set strange access timestamps on files, which
                // may lead to this exception being thrown. Since the last accessed
                // timestamp is not important such exeptions are just silently ignored.
                lastAccess = fileInfo.LastAccessTime;
            }
            catch { }
            return new FileEntry(fileInfo.Name, fileInfo.Length, lastAccess, fileInfo.LastWriteTime);
        }

        public Dictionary<string, string> GetMetadata(string path, bool isSymlink, bool followSymlink)
        {
            var isDirTarget = path.EndsWith(DIRSEP, StringComparison.Ordinal);
            var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;
            var dict = new Dictionary<string, string>();

            FileSystemSecurity rules = isDirTarget ? GetAccessControlDir(targetpath) : GetAccessControlFile(targetpath);
            var objs = new List<FileSystemAccess>();
            foreach (var f in rules.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier)))
                objs.Add(new FileSystemAccess((FileSystemAccessRule)f));

            dict["win-ext:accessrules"] = SerializeObject(objs);

            // Only include the following key when its value is True.
            // This prevents unnecessary 'metadata change' detections when upgrading from
            // older versions (pre-2.0.5.101) that didn't store this value at all.
            // When key is not present, its value is presumed False by the restore code.
            if (rules.AreAccessRulesProtected)
            {
                dict["win-ext:accessrulesprotected"] = "True";
            }

            return dict;
        }

        public void SetMetadata(string path, Dictionary<string, string> data, bool restorePermissions)
        {
            var isDirTarget = path.EndsWith(DIRSEP, StringComparison.Ordinal);
            var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;

            if (restorePermissions)
            {
                FileSystemSecurity rules = isDirTarget ? GetAccessControlDir(targetpath) : GetAccessControlFile(targetpath);

                if (data.ContainsKey("win-ext:accessrulesprotected"))
                {
                    bool isProtected = bool.Parse(data["win-ext:accessrulesprotected"]);
                    if (rules.AreAccessRulesProtected != isProtected)
                    {
                        rules.SetAccessRuleProtection(isProtected, false);
                    }
                }

                if (data.ContainsKey("win-ext:accessrules"))
                {
                    var content = DeserializeObject<FileSystemAccess[]>(data["win-ext:accessrules"]);
                    var c = rules.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier));
                    for (var i = c.Count - 1; i >= 0; i--)
                        rules.RemoveAccessRule((System.Security.AccessControl.FileSystemAccessRule)c[i]);

                    Exception ex = null;

                    foreach (var r in content)
                    {
                        // Attempt to apply as many rules as we can
                        try
                        {
                            rules.AddAccessRule(r.Create(rules));
                        }
                        catch (Exception e)
                        {
                            ex = e;
                        }
                    }

                    if (ex != null)
                        throw ex;
                }

                if (isDirTarget)
                    SetAccessControlDir(targetpath, (DirectorySecurity)rules);
                else
                    SetAccessControlFile(targetpath, (FileSecurity)rules);
            }
        }

        public string PathCombine(params string[] paths)
        {
            return Path.Combine(paths);
        }

        /// <summary>
        /// 创建一个符号链接。
        /// </summary>
        /// <param name="symlinkFile">符号链接文件的路径。</param>
        /// <param name="target">符号链接指向的目标路径。</param>
        /// <param name="asDir">如果创建的是指向目录的符号链接，则为true；如果是指向文件的符号链接，则为false。</param>
        public void CreateSymlink(string symlinkFile, string target, bool asDir)
        {
            // 检查符号链接文件是否已存在
            if (File.Exists(symlinkFile) || Directory.Exists(symlinkFile))
                throw new IOException($"文件已存在: {symlinkFile}");

            try
            {
                if (asDir)
                {
                    // 创建指向目录的符号链接
                    Directory.CreateSymbolicLink(symlinkFile, target);
                }
                else
                {
                    // 创建指向文件的符号链接
                    File.CreateSymbolicLink(symlinkFile, target);
                }
            }
            catch (Exception ex)
            {
                // 如果创建符号链接失败，抛出异常
                throw new IOException($"无法创建符号链接，检查账户权限: {symlinkFile}", ex);
            }

            // 验证符号链接是否成功创建
            if (!((asDir && Directory.Exists(symlinkFile)) || (!asDir && File.Exists(symlinkFile))))
            {
                throw new IOException($"无法验证符号链接是否创建成功: {symlinkFile}");
            }
        }

        #endregion ISystemIO implementation
    }
}