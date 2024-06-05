namespace MDriveSync.Security
{
    public static class Base64Extensions
    {
        /// <summary>
        /// 将字节数组编码为 URL 安全的 Base64 字符串。
        /// </summary>
        /// <param name="data">要编码的字节数组。</param>
        /// <returns>编码后的 URL 安全 Base64 字符串。</returns>
        public static string ToSafeBase64(this byte[] data)
        {
            string base64Encoded = Convert.ToBase64String(data);
            return base64Encoded.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        /// <summary>
        /// 从 URL 安全的 Base64 字符串解码为字节数组。
        /// </summary>
        /// <param name="urlSafeBase64">URL 安全的 Base64 字符串。</param>
        /// <returns>解码后的字节数组。</returns>
        public static byte[] FromSafeBase64(this string urlSafeBase64)
        {
            string standardBase64 = urlSafeBase64.Replace('-', '+').Replace('_', '/');

            // 计算必要的填充字符
            switch (standardBase64.Length % 4)
            {
                case 2: standardBase64 += "=="; break;
                case 3: standardBase64 += "="; break;
            }

            return Convert.FromBase64String(standardBase64);
        }
    }
}