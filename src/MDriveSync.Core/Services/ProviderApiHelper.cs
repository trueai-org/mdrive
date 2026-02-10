using MDriveSync.Core.Models;
using MDriveSync.Core.ViewModels;
using RestSharp;
using Serilog;
using System.Net;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 服务商 API
    /// </summary>
    public static class ProviderApiHelper
    {
        /// <summary>
        /// 阿里云盘 API
        /// </summary>
        public const string ALIYUNDRIVE_API_HOST = "https://openapi.alipan.com";

        /// <summary>
        /// DUPLICATI 服务商 API
        /// </summary>
        public const string DUPLICATI_SERVER_API_HOST = "https://api.duplicati.net";

        /// <summary>
        /// 百度网盘 API
        /// </summary>
        public const string BAIDU_NETDISK_API_HOST = "https://openapi.baidu.com";

        /// <summary>
        /// 获取登录授权二维码
        /// </summary>
        /// <returns></returns>
        public static AliyunDriveOAuthAuthorizeQrCodeResponse GetAuthQrcode()
        {
            var client = new RestClient(new RestClientOptions(DUPLICATI_SERVER_API_HOST)
            {
                MaxTimeout = -1
            });
            var request = new RestRequest("/api/open/aliyundrive/qrcode", Method.Get);
            var response = client.Execute<AliyunDriveOAuthAuthorizeQrCodeResponse>(request);
            if (response.StatusCode == HttpStatusCode.OK && response.Data != null)
            {
                return response.Data;
            }

            throw response?.ErrorException ?? new Exception("获取登录授权二维码失败，请重试");
        }

        /// <summary>
        /// 刷新请求令牌
        /// </summary>
        /// <returns></returns>
        public static AliyunDriveOAuthAccessToken RefreshToken(string refreshToken)
        {
            // 重新获取令牌
            var options = new RestClientOptions(DUPLICATI_SERVER_API_HOST)
            {
                MaxTimeout = -1
            };
            var client = new RestClient(options);
            var request = new RestRequest($"/api/open/aliyundrive/refresh-token", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            var body = new RefreshTokenRequest
            {
                RefreshToken = refreshToken
            };
            request.AddBody(body);
            var response = client.Execute<AliyunDriveOAuthAccessToken>(request);

            // 记录日志
            Log.Information("使用刷新令牌请求新令牌，刷新令牌：{@0}，状态码：{@1}, 响应内容：{@2}", refreshToken,
                response.StatusCode, response.Content);

            if (response.StatusCode == HttpStatusCode.OK && response.Data != null)
            {
                return response.Data;
            }

            throw response?.ErrorException ?? new Exception("刷新请求令牌失败，请重试");
        }
    }
}