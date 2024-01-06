namespace MDriveSync.Core.IO
{
    // 静态部分类，提供扩展方法
    public static partial class Extensions
    {
        /// <summary>
        /// 用于ISystemIO的扩展方法，用于确定给定路径是否是符号链接。
        /// </summary>
        /// <param name="systemIO">ISystemIO实现</param>
        /// <param name="path">文件或文件夹路径</param>
        /// <returns>该路径是否是符号链接</returns>
        public static bool IsSymlink(this ISystemIO systemIO, string path)
        {
            return systemIO.IsSymlink(path, systemIO.GetFileAttributes(path));
        }

        /// <summary>
        /// 用于ISystemIO的扩展方法，用于确定给定路径是否是符号链接。
        /// </summary>
        /// <param name="systemIO">ISystemIO实现</param>
        /// <param name="path">文件或文件夹路径</param>
        /// <param name="attributes">文件属性</param>
        /// <returns>该路径是否是符号链接</returns>
        public static bool IsSymlink(this ISystemIO systemIO, string path, FileAttributes attributes)
        {
            // 并非所有的重解析点都是符号链接。
            // 例如，在Windows 10 Fall Creator's Update中，OneDrive文件夹（及其所有子文件夹）是重解析点，
            // 这允许文件夹钩入OneDrive服务并按需下载内容。
            // 如果我们无法为当前路径找到符号链接目标，我们不会将其视为符号链接。
            return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint && !string.IsNullOrEmpty(systemIO.GetSymlinkTarget(path));
        }

        /// <summary>
        /// 合并两个序列的扩展方法。
        /// </summary>
        /// <param name="first">第一个序列</param>
        /// <param name="second">第二个序列</param>
        /// <returns>合并后的序列</returns>
        public static IEnumerable<TSource> Union<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second)
        {
            return UnionIterator(first, second, null);
        }

        // 实现Union方法的私有迭代器
        private static IEnumerable<TSource> UnionIterator<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        {
            var set = comparer != null ? new HashSet<TSource>(comparer) : new HashSet<TSource>();

            foreach (TSource item in first)
            {
                if (set.Add(item))
                {
                    yield return item;
                }
            }

            foreach (TSource item in second)
            {
                if (set.Add(item))
                {
                    yield return item;
                }
            }
        }


        /// <summary>
        /// The regular expression that matches %20 type values in a querystring
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex RE_NUMBER = new System.Text.RegularExpressions.Regex(@"(\%(?<number>([0-9]|[a-f]|[A-F]){2}))|(\+)|(\%u(?<number>([0-9]|[a-f]|[A-F]){4}))", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Decodes a URL, like System.Web.HttpUtility.UrlDecode
        /// </summary>
        /// <returns>The decoded URL</returns>
        /// <param name="value">The URL fragment to decode</param>
        /// <param name="encoding">The encoding to use</param>
        public static string UrlDecode(this string value, System.Text.Encoding encoding = null)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            encoding = encoding ?? System.Text.Encoding.UTF8;

            var decoder = encoding.GetDecoder();
            var inbuf = new byte[8];
            var outbuf = new char[8];

            return RE_NUMBER.Replace(value, (m) =>
            {
                if (m.Value == "+")
                    return " ";

                try
                {
                    var hex = m.Groups["number"].Value;
                    var bytelen = hex.Length / 2;
                    HexStringAsByteArray(hex, inbuf);
                    var c = decoder.GetChars(inbuf, 0, bytelen, outbuf, 0);
                    return new string(outbuf, 0, c);
                }
                catch
                {
                }

                //Fallback
                return m.Value;
            });

        }

        /// <summary>
        /// Converts a hex string to a byte array
        /// </summary>
        /// <returns>The string as byte array.</returns>
        /// <param name="hex">The hex string</param>
        /// <param name="data">The parsed data</param>
        public static void HexStringAsByteArray(string hex, byte[] data)
        {
            for (var i = 0; i < hex.Length; i += 2)
                data[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }
    }
}
