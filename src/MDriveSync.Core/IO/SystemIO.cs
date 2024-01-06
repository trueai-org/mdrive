namespace MDriveSync.Core.IO
{
    /// <summary>
    /// 该类提供了对不同操作系统上 I/O 操作的支持，通过 IO_WIN、IO_SYS 和 IO_OS 来区分不同的系统实现。
    /// </summary>
    public static class SystemIO
    {
        /// <summary>
        /// 用于处理长文件名的 Windows 方法的缓存查找
        /// </summary>
        public static readonly ISystemIO IO_WIN;

        /// <summary>
        /// 表示系统 I/O 的静态成员
        /// </summary>
        public static readonly ISystemIO IO_SYS;

        /// <summary>
        /// 根据操作系统选择合适的 I/O 实现
        /// </summary>
        public static readonly ISystemIO IO_OS;

        static SystemIO()
        {
            // 初始化 Windows 特定的 I/O 实现
            IO_WIN = new SystemIOWindows();

            // 初始化 Linux 特定的 I/O 实现
            IO_SYS = new SystemIOLinux();

            // 根据当前操作系统选择使用 Windows 或 Linux 的 I/O 实现
            IO_OS = Platform.IsClientWindows ? IO_WIN : IO_SYS;
        }
    }
}