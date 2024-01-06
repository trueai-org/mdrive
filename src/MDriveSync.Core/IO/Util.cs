namespace MDriveSync.Core.IO
{
    public static class Util
    {
        /// <summary>
        /// 缓存的目录分隔符实例，以字符串形式表示
        /// </summary>
        public static readonly string DirectorySeparatorString = Path.DirectorySeparatorChar.ToString();

        public static readonly string AltDirectorySeparatorString = Path.AltDirectorySeparatorChar.ToString();

        /// <summary>
        /// 根据操作系统，在路径后附加适当的目录分隔符。
        /// 如果路径已经以分隔符结尾，则不附加分隔符。
        /// </summary>
        /// <param name="path">要附加的路径</param>
        /// <returns>附加了目录分隔符的路径</returns>
        public static string AppendDirSeparator(string path)
        {
            return AppendDirSeparator(path, DirectorySeparatorString);
        }

        /// <summary>
        /// 附加指定的目录分隔符到路径。
        /// 如果路径已经以分隔符结尾，则不附加分隔符。
        /// </summary>
        /// <param name="path">要附加的路径</param>
        /// <param name="separator">使用的目录分隔符</param>
        /// <returns>附加了目录分隔符的路径</returns>
        public static string AppendDirSeparator(string path, string separator)
        {
            // 如果路径未以指定分隔符结尾，则附加分隔符
            return !path.EndsWith(separator, StringComparison.Ordinal) ? path + separator : path;
        }

        /// <summary>
        /// 从路径猜测目录分隔符
        /// </summary>
        /// <param name="path">要猜测分隔符的路径</param>
        /// <returns>猜测的目录分隔符</returns>
        public static string GuessDirSeparator(string path)
        {
            // 如果路径为空或以斜杠("/")开头，则返回斜杠("/")，否则返回反斜杠("\\")
            return string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal) ? "/" : "\\";
        }
    }
}
