namespace MDriveSync.Core.IO
{
    // 特殊文件夹类
    public static class SpecialFolders
    {
        // 存储树节点数组
        public static readonly TreeNode[] Nodes;

        // 路径映射字典，用于存储路径的映射
        private static readonly Dictionary<string, string> PathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 显示名映射字典，用于存储显示名的映射
        private static readonly Dictionary<string, string> DisplayMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 展开环境变量的方法
        public static string ExpandEnvironmentVariables(string path)
        {
            foreach (var n in Nodes)
            {
                if (path.StartsWith(n.id, StringComparison.Ordinal))
                {
                    path = path.Replace(n.id, n.resolvedpath);
                }
            }
            return Environment.ExpandEnvironmentVariables(path);
        }

        // 将字符串翻译为路径的方法
        public static string TranslateToPath(string str)
        {
            string res;
            if (PathMap.TryGetValue(str, out res))
            {
                return res;
            }
            return null;
        }

        // 将字符串翻译为显示字符串的方法
        public static string TranslateToDisplayString(string str)
        {
            string res;
            if (DisplayMap.TryGetValue(str, out res))
            {
                return res;
            }
            return null;
        }

        // 尝试添加节点到列表中的方法（使用特殊文件夹枚举）
        private static void TryAdd(List<TreeNode> lst, System.Environment.SpecialFolder folder, string id, string display)
        {
            try
            {
                TryAdd(lst, System.Environment.GetFolderPath(folder), id, display);
            }
            catch
            {
                // 异常处理留空
            }
        }

        // 尝试添加节点到列表中的方法（重载，使用文件夹路径）
        private static void TryAdd(List<TreeNode> lst, string folder, string id, string display)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(folder) && System.IO.Path.IsPathRooted(folder) && System.IO.Directory.Exists(folder))
                {
                    if (!PathMap.ContainsKey(id))
                    {
                        lst.Add(new TreeNode()
                        {
                            id = id,
                            text = display,
                            leaf = false,
                            iconCls = "x-tree-icon-special",
                            resolvedpath = folder
                        });

                        PathMap[id] = folder;
                        DisplayMap[id] = display;
                    }
                }
            }
            catch
            {
                // 异常处理留空
            }
        }

        // 静态构造函数
        static SpecialFolders()
        {
            var lst = new List<TreeNode>();

            // 根据平台类型添加特定的文件夹
            if (Platform.IsClientWindows)
            {
                // Windows平台的特殊文件夹
                TryAdd(lst, Environment.SpecialFolder.MyDocuments, "%MY_DOCUMENTS%", "My Documents");
                TryAdd(lst, Environment.SpecialFolder.MyMusic, "%MY_MUSIC%", "My Music");
                TryAdd(lst, Environment.SpecialFolder.MyPictures, "%MY_PICTURES%", "My Pictures");
                TryAdd(lst, Environment.SpecialFolder.MyVideos, "%MY_VIDEOS%", "My Videos");
                TryAdd(lst, Environment.SpecialFolder.DesktopDirectory, "%DESKTOP%", "Desktop");
                TryAdd(lst, Environment.SpecialFolder.ApplicationData, "%APPDATA%", "Application Data");
                TryAdd(lst, Environment.SpecialFolder.UserProfile, "%HOME%", "Home");

                try
                {
                    // 在UserProfile成员指向无效内容时的处理
                    TryAdd(lst, System.IO.Path.Combine(Environment.GetEnvironmentVariable("HOMEDRIVE"), Environment.GetEnvironmentVariable("HOMEPATH")), "%HOME%", "Home");
                }
                catch
                {
                    // 异常处理留空
                }
            }
            else
            {
                // 非Windows平台的特殊文件夹
                TryAdd(lst, Environment.SpecialFolder.MyDocuments, "%MY_DOCUMENTS%", "My Documents");
                TryAdd(lst, Environment.SpecialFolder.MyMusic, "%MY_MUSIC%", "My Music");
                TryAdd(lst, Environment.SpecialFolder.MyPictures, "%MY_PICTURES%", "My Pictures");
                TryAdd(lst, Environment.SpecialFolder.DesktopDirectory, "%DESKTOP%", "Desktop");
                TryAdd(lst, Environment.GetEnvironmentVariable("HOME"), "%HOME%", "Home");
                TryAdd(lst, Environment.SpecialFolder.Personal, "%HOME%", "Home");
            }

            Nodes = lst.ToArray();
        }

        // 获取源名称的字典的内部静态方法
        internal static Dictionary<string, string> GetSourceNames(IBackup backup)
        {
            if (backup.Sources == null || backup.Sources.Length == 0)
                return new Dictionary<string, string>();

            var sources = backup.Sources.Distinct().Select(x =>
            {
                var sp = SpecialFolders.TranslateToDisplayString(x);
                if (sp != null)
                    return new KeyValuePair<string, string>(x, sp);

                x = SpecialFolders.ExpandEnvironmentVariables(x);
                try
                {
                    var nx = x;
                    if (nx.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal))
                        nx = nx.Substring(0, nx.Length - 1);
                    var n = SystemIO.IO_OS.PathGetFileName(nx);
                    if (!string.IsNullOrWhiteSpace(n))
                        return new KeyValuePair<string, string>(x, n);
                }
                catch
                {
                    // 异常处理留空
                }

                if (x.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal) && x.Length > 1)
                    return new KeyValuePair<string, string>(x, x.Substring(0, x.Length - 1).Substring(x.Substring(0, x.Length - 1).LastIndexOf("/", StringComparison.Ordinal) + 1));
                else
                    return new KeyValuePair<string, string>(x, x);
            });

            // 处理重复项
            var result = new Dictionary<string, string>();
            foreach (var x in sources)
                result[x.Key] = x.Value;

            return result;
        }
    }
}