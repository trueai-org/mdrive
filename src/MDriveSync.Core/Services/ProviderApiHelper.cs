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
            if (response.StatusCode == HttpStatusCode.OK && response.Data != null)
            {
                return response.Data;
            }

            throw response?.ErrorException ?? new Exception("刷新请求令牌失败，请重试");
        }

        /// <summary>
        /// 文件删除或放入回收站
        /// https://www.yuque.com/aliyundrive/zpfszx/get3mkr677pf10ws
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="fileId"></param>
        /// <param name="accessToken"></param>
        /// <param name="isRecycleBin">是否放入回收站，默认：false</param>
        public static AliyunDriveOpenFileDeleteResponse FileDelete(string driveId, string fileId, string accessToken, bool isRecycleBin = false)
        {
            var options = new RestClientOptions(ALIYUNDRIVE_API_HOST)
            {
                MaxTimeout = -1
            };
            var client = new RestClient(options);

            // 不放入回收站
            var resource = "/adrive/v1.0/openFile/delete";
            if (isRecycleBin)
            {
                resource = "/adrive/v1.0/openFile/recyclebin/trash";
            }

            var request = new RestRequest(resource, Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {accessToken}");
            object body = new
            {
                drive_id = driveId,
                file_id = fileId
            };
            request.AddBody(body);
            var response = client.Execute<AliyunDriveOpenFileDeleteResponse>(request);
            if (response.StatusCode == HttpStatusCode.OK && response.Data != null)
            {
                return response.Data;
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            throw response?.ErrorException ?? new Exception("文件删除失败，请重试");
        }

        /// <summary>
        /// 文件更新
        /// https://www.yuque.com/aliyundrive/zpfszx/dp9gn443hh8oksgd
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="fileId"></param>
        /// <param name="name"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public static AliyunDriveFileItem FileUpdate(string driveId, string fileId, string name, string accessToken,
            string check_name_mode = "refuse")
        {
            var options = new RestClientOptions(ALIYUNDRIVE_API_HOST)
            {
                MaxTimeout = -1
            };
            var client = new RestClient(options);
            var request = new RestRequest($"/adrive/v1.0/openFile/update", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {accessToken}");
            object body = new
            {
                drive_id = driveId,
                file_id = fileId,
                name = name,

                // refuse 同名不创建
                // ignore 同名文件可创建
                check_name_mode = check_name_mode
            };
            request.AddBody(body);
            var response = client.Execute<AliyunDriveFileItem>(request);
            if (response.StatusCode == HttpStatusCode.OK && response.Data != null)
            {
                return response.Data;
            }

            throw response?.ErrorException ?? new Exception("文件更新失败，请重试");
        }
    }
}