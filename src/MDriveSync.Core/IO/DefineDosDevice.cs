namespace MDriveSync.Core.IO
{
    /// <summary>
    /// 为映射路径到驱动器字母提供方便的封装。
    /// </summary>
    public class DefineDosDevice : IDisposable
    {
        /// <summary>
        /// 封装Win32调用的内部类。
        /// </summary>
        private static class Win32API
        {
            /// <summary>
            /// 可以用于 DefineDosDevice 的标志。
            /// </summary>
            [Flags]
            public enum DDD_Flags : uint
            {
                /// <summary>
                /// 使用 targetpath 字符串原样。否则，它会从 MS-DOS 路径转换为路径。
                /// </summary>
                DDD_RAW_TARGET_PATH = 0x1,

                /// <summary>
                /// 删除指定设备的指定定义。为确定要删除哪个定义，函数遍历设备映射列表，查找 targetpath 与该设备关联的每个映射的前缀的匹配项。第一个匹配的映射被删除，然后函数返回。
                /// 如果 targetpath 为 NULL 或指向 NULL 字符串的指针，函数将删除与设备关联的第一个映射，并弹出最近推送的映射。如果没有剩余的映射可弹出，则删除设备名称。
                /// 如果未指定此值，则 targetpath 参数指向的字符串将成为此设备的新映射。
                /// </summary>
                DDD_REMOVE_DEFINITION = 0x2,

                /// <summary>
                /// 如果指定了此值和 DDD_REMOVE_DEFINITION，则函数将使用精确匹配来确定要删除哪个映射。使用此值以确保您不会删除未定义的内容。
                /// </summary>
                DDD_EXACT_MATCH_ON_REMOVE = 0x4,

                /// <summary>
                /// 不广播 WM_SETTINGCHANGE 消息。默认情况下，此消息被广播以通知 shell 和应用程序更改。
                /// </summary>
                DDD_NO_BROADCAST_SYSTEM = 0x8
            }

            /// <summary>
            /// 定义、重新定义或删除 MS-DOS 设备名称。
            /// </summary>
            /// <param name="flags">DefineDosDevice 函数的可控方面</param>
            /// <param name="devicename">指定函数定义、重新定义或删除的 MS-DOS 设备名称字符串的指针。设备名称字符串的最后一个字符不能是冒号，除非定义、重新定义或删除的是驱动器字母。例如，驱动器 C 将是字符串 "C:"。在任何情况下都不允许尾随反斜杠 ("\")。</param>
            /// <param name="targetpath">实现此设备的路径字符串的指针。字符串是 MS-DOS 路径字符串，除非指定了 DDD_RAW_TARGET_PATH 标志，在这种情况下，此字符串是路径字符串。</param>
            /// <returns>成功时为 True，否则为 False</returns>
            [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
            public static extern bool DefineDosDevice(DDD_Flags flags, string devicename, string targetpath);
        }

        /// <summary>
        /// 映射到的驱动器。
        /// </summary>
        private string m_drive;

        /// <summary>
        /// 映射到驱动器的路径。
        /// </summary>
        private readonly string m_targetPath;

        /// <summary>
        /// 指示是否应通知 shell 更改的值。
        /// </summary>
        private readonly bool m_shellBroadcast;

        /// <summary>
        /// 获取此映射代表的驱动器。
        /// </summary>
        public string Drive
        {
            get { return m_drive; }
        }

        /// <summary>
        /// 使用默认设置创建新映射的构造函数。
        /// </summary>
        /// <param name="path">要映射的路径</param>
        public DefineDosDevice(string path)
            : this(path, null, false)
        {
        }

        /// <summary>
        /// 创建新映射的构造函数。
        /// </summary>
        /// <param name="path">要映射的路径</param>
        /// <param name="drive">要映射到的驱动器，使用 null 获取一个空闲的驱动器字母</param>
        /// <param name="notifyShell">True 表示通知 shell 更改，False 表示不通知</param>
        public DefineDosDevice(string path, string drive, bool notifyShell)
        {
            if (string.IsNullOrEmpty(drive))
            {
                List<char> drives = new List<char>("DEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray());
                foreach (DriveInfo di in DriveInfo.GetDrives())
                {
                    if ((di.RootDirectory.FullName.Length == 2 && di.RootDirectory.FullName[1] == ':') || ((di.RootDirectory.FullName.Length == 3 && di.RootDirectory.FullName.EndsWith(":\\", StringComparison.Ordinal))))
                    {
                        int i = drives.IndexOf(di.RootDirectory.FullName[0]);
                        if (i >= 0)
                            drives.RemoveAt(i);
                    }
                }

                if (drives.Count == 0)
                    throw new IOException("没有可用的驱动器字母");
                drive = drives[0].ToString() + ':';
            }

            while (drive.EndsWith("\\", StringComparison.Ordinal))
                drive = drive.Substring(0, drive.Length - 1);

            if (!drive.EndsWith(":", StringComparison.Ordinal))
                throw new ArgumentException("驱动器规范必须以冒号结尾。", nameof(drive));

            Win32API.DDD_Flags flags = 0;
            if (!notifyShell)
                flags |= Win32API.DDD_Flags.DDD_NO_BROADCAST_SYSTEM;

            if (!Win32API.DefineDosDevice(flags, drive, path))
                throw new System.ComponentModel.Win32Exception();

            m_drive = drive;
            m_targetPath = path;
            m_shellBroadcast = notifyShell;
        }

        /// <summary>
        /// 释放所有资源。
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// 释放所有资源。
        /// </summary>
        /// <param name="disposing">如果从 Dispose 方法调用，则为 True；否则为 False</param>
        protected void Dispose(bool disposing)
        {
            if (m_drive != null)
            {
                Win32API.DDD_Flags flags = Win32API.DDD_Flags.DDD_REMOVE_DEFINITION | Win32API.DDD_Flags.DDD_EXACT_MATCH_ON_REMOVE;
                if (m_shellBroadcast)
                    flags |= Win32API.DDD_Flags.DDD_NO_BROADCAST_SYSTEM;
                Win32API.DefineDosDevice(flags, m_drive, m_targetPath);
                m_drive = null;
            }

            if (disposing)
                GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 析构函数，用于释放资源。
        /// </summary>
        ~DefineDosDevice()
        {
            Dispose(false);
        }
    }
}