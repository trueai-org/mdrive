using System.Text.Json.Serialization;

namespace MDriveSync.Core
{
    /// <summary>
    /// 获取文件下载链接
    /// https://www.yuque.com/aliyundrive/zpfszx/gogo34oi2gy98w5d
    /// </summary>
    public class AliyunDriveOpenFileGetDownloadUrlResponse
    {
        /// <summary>
        /// 下载地址
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; }

        /// <summary>
        /// 过期时间 格式："yyyy-MM-dd'T'HH:mm:ss.SSS'Z'"
        /// </summary>
        [JsonPropertyName("expiration")]
        public string Expiration { get; set; }

        /// <summary>
        /// 下载方法
        /// </summary>
        [JsonPropertyName("method")]
        public string Method { get; set; }
    }
}