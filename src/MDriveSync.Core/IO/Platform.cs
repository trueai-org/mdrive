namespace MDriveSync.Core.IO
{
    /// <summary>
    /// 平台
    /// </summary>
    public static class Platform
    {
        /// <value>
        /// 获取或设置一个值，该值指示客户端是否基于 Linux/Unix
        /// </value>
        public static readonly bool IsClientPosix;

        /// <summary>
        /// 获取一个值，该值指示客户端是否基于 Windows
        /// </summary>
        public static readonly bool IsClientWindows;

        /// <value>
        /// 获取或设置一个值，该值指示客户端是否运行在 OSX 上
        /// </value>
        public static readonly bool IsClientOSX;

        static Platform()
        {
            // 判断当前操作系统是否为 Unix 或 MacOSX
            IsClientPosix = Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX;

            // 判断当前操作系统是否为 Windows
            IsClientWindows = !IsClientPosix;

            // 判断当前操作系统是否为 OSX
            IsClientOSX = IsClientPosix && "Darwin".Equals(_RetrieveUname(false));
        }

        /// <value>
        /// 获取 Linux 上 "uname -a" 的输出结果，或在 Windows 上获取 null
        /// </value>
        public static string UnameAll
        {
            get
            {
                // 如果客户端不是基于 Posix 的系统，则返回 null
                if (!IsClientPosix)
                    return null;

                // 获取并返回 uname 命令的输出
                return _RetrieveUname(true);
            }
        }

        private static string _RetrieveUname(bool showAll)
        {
            try
            {
                // 创建一个新的进程启动信息，并执行 uname 命令
                var psi = new System.Diagnostics.ProcessStartInfo("uname", showAll ? "-a" : null)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = false,
                    RedirectStandardInput = false,
                    UseShellExecute = false
                };

                // 启动进程并等待其结束
                var pi = System.Diagnostics.Process.Start(psi);
                pi.WaitForExit(5000);

                // 如果进程已经退出，返回标准输出的内容
                if (pi.HasExited)
                    return pi.StandardOutput.ReadToEnd().Trim();
            }
            catch
            {
                // 如果发生异常，返回 null
                return null;
            }

            // 默认返回 null
            return null;
        }
    }
}