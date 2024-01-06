namespace MDriveSync.Core.IO
{
    public static class Platform
    {
        /// <value>
        /// Gets or sets a value indicating if the client is Linux/Unix based
        /// </value>
        public static readonly bool IsClientPosix;

        /// <summary>
        /// Gets a value indicating if the client is Windows based
        /// </summary>
        public static readonly bool IsClientWindows;

        /// <value>
        /// Gets or sets a value indicating if the client is running OSX
        /// </value>
        public static readonly bool IsClientOSX;

        static Platform()
        {
            IsClientPosix = Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX;
            IsClientWindows = !IsClientPosix;
            IsClientOSX = IsClientPosix && "Darwin".Equals(_RetrieveUname(false));
        }

        /// <value>
        /// Gets the output of "uname -a" on Linux, or null on Windows
        /// </value>
        public static string UnameAll
        {
            get
            {
                if (!IsClientPosix)
                    return null;

                return _RetrieveUname(true);
            }
        }

        private static string _RetrieveUname(bool showAll)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("uname", showAll ? "-a" : null)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = false,
                    RedirectStandardInput = false,
                    UseShellExecute = false
                };

                var pi = System.Diagnostics.Process.Start(psi);
                pi.WaitForExit(5000);
                if (pi.HasExited)
                    return pi.StandardOutput.ReadToEnd().Trim();
            }
            catch
            {
                return null;
            }

            return null;
        }
    }
}