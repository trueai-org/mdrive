namespace MDriveSync.Core.IO
{
    // .NET运行时路径Windows类
    public static class DotNetRuntimePathWindows
    {
        /// <summary>
        /// 如果路径固定到特定的驱动器或UNC路径，则返回true。此方法不对路径进行验证（因此，URI会被认为是相对的）。
        /// 如果指定的路径相对于当前驱动器或工作目录，则返回false。
        /// </summary>
        /// <remarks>
        /// 处理使用备用目录分隔符的路径。常见的错误是假设根路径 <see cref="Path.IsPathRooted(string)"/> 不是相对的。但这不是情况。
        /// "C:a" 是相对于C:的当前目录的驱动器相对路径（根，但相对）。
        /// "C:\a" 是根的，不是相对的（当前目录不会用来修改路径）。
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// 如果 <paramref name="path"/> 是null，则抛出。
        /// </exception>
        public static bool IsPathFullyQualified(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            // 调用PathInternalWindows类中的IsPartiallyQualified方法来判断路径是否部分限定
            return !PathInternalWindows.IsPartiallyQualified(path);
        }
    }
}
