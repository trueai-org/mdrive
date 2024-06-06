namespace MDriveSync.Security
{
    public static partial class Extensions
    {
        private static readonly char[] PathSeparator = ['/'];

        /// <summary>
        /// 移除路径首尾 ' ', '/', '\'
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string TrimPath(this string path)
        {
            return path?.Trim().Trim('/').Trim('\\').Trim('/').Trim();
        }

        /// <summary>
        /// 转为 url 路径
        /// 例如：由 E:\_backups\p00\3e4 -> _backups/p00/3e4
        /// </summary>
        /// <param name="path"></param>
        /// <param name="removePrefix">移除的前缀</param>
        /// <returns></returns>
        public static string ToUrlPath(this string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            // 替换所有的反斜杠为斜杠
            // 分割路径，移除空字符串，然后重新连接
            return string.Join("/", path.Replace("\\", "/").Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries)).TrimPath();
        }

        /// <summary>
        /// 将完整路径分解为子路径列表
        /// 例如：/a/b/c -> [a, b, c]
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string[] ToSubPaths(this string path)
        {
            return path?.ToUrlPath().Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        }
    }
}