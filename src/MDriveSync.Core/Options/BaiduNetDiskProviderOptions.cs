namespace MDriveSync.Core
{
    /// <summary>
    /// 百度网盘服务商
    /// </summary>
    public class BaiduNetDiskProviderOptions
    {
        /// <summary>
        /// 应用 ID
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// 应用密钥
        /// </summary>
        public string ClientSecret { get; set; }
    }
}