namespace MDriveSync.Core.IO
{
    public static class SystemIO
    {

        /// <summary>
        /// A cached lookup for windows methods for dealing with long filenames
        /// </summary>
        public static readonly ISystemIO IO_WIN;

        public static readonly ISystemIO IO_SYS;

        public static readonly ISystemIO IO_OS;

        static SystemIO()
        {
            IO_WIN = new SystemIOWindows();
            IO_SYS = new SystemIOLinux();
            IO_OS = Platform.IsClientWindows ? IO_WIN : IO_SYS;
        }
    }
}