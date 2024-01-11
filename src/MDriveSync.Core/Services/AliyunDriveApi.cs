using MDriveSync.Core.Models;
using RestSharp;
using Serilog;
using System.Net;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 阿里云盘 API
    /// https://www.yuque.com/aliyundrive/zpfszx/ezlzok
    /// </summary>
    public class AliyunDriveApi
    {
        /// <summary>
        /// 阿里云盘 API
        /// </summary>
        public const string ALIYUNDRIVE_API_HOST = "https://openapi.alipan.com";

        /// <summary>
        /// 设置列表每次请求之间的间隔为 250ms
        /// 当触发限流时
        /// </summary>
        public const int REQUEST_INTERVAL = 250;

        /// <summary>
        /// 接口请求
        /// </summary>
        private readonly RestClient _apiClient;

        private readonly ILogger _log;

        public AliyunDriveApi()
        {
            _log = Log.Logger;

            // 接口请求
            var options = new RestClientOptions(ALIYUNDRIVE_API_HOST)
            {
                MaxTimeout = -1
            };
            _apiClient = new RestClient(options);
        }

        /// <summary>
        /// 阿里云盘 - 获取用户空间信息
        /// </summary>
        /// <returns></returns>
        public AliyunDriveSpaceInfo SpaceInfo(string accessToken)
        {
            var request = new RestRequest("/adrive/v1.0/user/getSpaceInfo", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {accessToken}");
            var response = WithRetry<AliyunDriveSpaceInfo>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response.Data;
            }

            throw response?.ErrorException ?? new Exception("获取用户空间信息失败，请重试");
        }

        /// <summary>
        /// 阿里云盘 - 获取用户 VIP 信息
        /// </summary>
        /// <returns></returns>
        public AliyunDriveVipInfo VipInfo(string accessToken)
        {
            var request = new RestRequest("/v1.0/user/getVipInfo", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {accessToken}");
            var response = WithRetry<AliyunDriveVipInfo>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response.Data;
            }
            throw response?.ErrorException ?? new Exception("获取用户 VIP 信息失败，请重试");
        }

        /// <summary>
        /// 阿里云盘 - 获取用户 drive 信息
        /// </summary>
        /// <returns></returns>
        public AliyunDriveInfo DriveInfo(string accessToken)
        {
            var request = new RestRequest("/adrive/v1.0/user/getDriveInfo", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {accessToken}");
            var response = _apiClient.Execute<AliyunDriveInfo>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response.Data;
            }

            throw response?.ErrorException ?? new Exception("获取用户 drive 信息失败，请重试");
        }

        /// <summary>
        /// 文件删除或放入回收站
        /// https://www.yuque.com/aliyundrive/zpfszx/get3mkr677pf10ws
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="fileId"></param>
        /// <param name="accessToken"></param>
        /// <param name="isRecycleBin">是否放入回收站，默认：false</param>
        public AliyunDriveOpenFileDeleteResponse FileDelete(string driveId, string fileId, string accessToken, bool isRecycleBin = false)
        {
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
            var response = WithRetry<AliyunDriveOpenFileDeleteResponse>(request);
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
        public AliyunDriveFileItem FileUpdate(string driveId, string fileId, string name, string accessToken, string check_name_mode = "refuse")
        {
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
            var response = WithRetry<AliyunDriveFileItem>(request);
            if (response.StatusCode == HttpStatusCode.OK && response.Data != null)
            {
                return response.Data;
            }

            throw response?.ErrorException ?? new Exception("文件更新失败，请重试");
        }

        /// <summary>
        /// 阿里云盘 - 获取文件下载 URL
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        public AliyunDriveOpenFileGetDownloadUrlResponse GetDownloadUrl(string driveId, string fileId, string accessToken, int sec = 14400)
        {
            var request = new RestRequest("/adrive/v1.0/openFile/getDownloadUrl", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {accessToken}");
            object body = new
            {
                drive_id = driveId,
                file_id = fileId,
                expire_sec = sec
            };
            request.AddBody(body);
            var response = WithRetry<AliyunDriveOpenFileGetDownloadUrlResponse>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response.Data;
            }

            throw response?.ErrorException ?? new Exception("获取文件下载链接失败，请重试");
        }

        /// <summary>
        /// 阿里云盘 - 判断文件是否存在
        /// </summary>
        /// <param name="parentFileId"></param>
        /// <param name="name"></param>
        /// <param name="type">folder | file</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public AliyunFileList Exist(string driveId, string parentFileId, string name, string accessToken, string type = "file")
        {
            var request = new RestRequest("/adrive/v1.0/openFile/search", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {accessToken}");

            object body = new
            {
                drive_id = driveId,
                query = $"parent_file_id='{parentFileId}' and type = '{type}' and name = '{name}'"
            };
            request.AddBody(body);
            var response = WithRetry<AliyunFileList>(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw response.ErrorException ?? new Exception(response.Content ?? "获取云盘目录失败");
            }

            return response.Data;
        }

        /// <summary>
        /// 阿里云盘 - 获取指定文件夹的子级文件夹
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="parentFileId">父级文件夹 ID</param>
        /// <param name="name">文件夹名称或子文件夹名称</param>
        /// <param name="accessToken"></param>
        /// <returns>如果查询到当前文件夹存在，则返回当前文件夹的子级文件夹</returns>
        public AliyunFileList GetSubFolders(string driveId, string parentFileId, string name, string accessToken)
        {
            var request = new RestRequest("/adrive/v1.0/openFile/search", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {accessToken}");

            object body = new
            {
                drive_id = driveId,
                query = $"parent_file_id='{parentFileId}' and type = 'folder' and name = '{name}'"
            };
            request.AddBody(body);
            var response = WithRetry<AliyunFileList>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response.Data;
            }

            throw response.ErrorException ?? new Exception(response.Content ?? "获取云盘目录失败");
        }

        /// <summary>
        /// 阿里云盘 - 获取文件/文件夹详情
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="fileId"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public T GetDetail<T>(string driveId, string fileId, string accessToken) where T : AliyunDriveFileItem
        {
            var request = new RestRequest("/adrive/v1.0/openFile/get", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {accessToken}");
            object body = new
            {
                drive_id = driveId,
                file_id = fileId
            };
            request.AddBody(body);
            var response = WithRetry<T>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response.Data;
            }

            throw response.ErrorException ?? new Exception(response.Content ?? "获取文件信息失败");
        }

        /// <summary>
        /// 阿里云盘 - 获取文件列表
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="parentFileId"></param>
        /// <param name="limit"></param>
        /// <param name="marker"></param>
        /// <param name="orderBy"></param>
        /// <param name="orderDirection"></param>
        /// <param name="category"></param>
        /// <param name="type"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public AliyunFileList FileList(string driveId, string parentFileId, int limit, string marker, string orderBy, string orderDirection, string category, string type, string accessToken)
        {
            var request = new RestRequest("/adrive/v1.0/openFile/list", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {accessToken}");
            var body = new
            {
                drive_id = driveId,
                parent_file_id = parentFileId,
                limit,
                marker,
                order_by = orderBy,
                order_direction = orderDirection,
                category,
                type
            };
            request.AddJsonBody(body);

            var response = WithRetry<AliyunFileList>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response.Data;
            }

            throw response.ErrorException ?? new Exception(response.Content ?? "获取文件列表失败");
        }

        /// <summary>
        /// 阿里云盘 - 上传标记完成
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="fileId"></param>
        /// <param name="uploadId"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public AliyunDriveFileItem UploadComplete(string driveId, string fileId, string uploadId, string accessToken)
        {
            var req = new RestRequest("/adrive/v1.0/openFile/complete", Method.Post);
            req.AddHeader("Content-Type", "application/json");
            req.AddHeader("Authorization", $"Bearer {accessToken}");
            var bodyCom = new
            {
                drive_id = driveId,
                file_id = fileId,
                upload_id = uploadId,
            };
            req.AddBody(bodyCom);
            var response = WithRetry<AliyunDriveFileItem>(req);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response.Data;
            }
            throw response.ErrorException ?? new Exception(response.Content ?? "上传标记完成失败");
        }

        /// <summary>
        /// 阿里云盘 - 搜索所有文件
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="limit"></param>
        /// <param name="marker"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public AliyunFileList SearchAllFileList(string driveId, int limit, string marker, string accessToken)
        {
            var request = new RestRequest("/adrive/v1.0/openFile/search", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {accessToken}");
            var body = new
            {
                drive_id = driveId,
                limit = limit,
                marker = marker,
                query = ""
            };
            request.AddJsonBody(body);
            var response = WithRetry<AliyunFileList>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response.Data;
            }
            throw response.ErrorException ?? new Exception(response.Content ?? "搜索文件失败");
        }

        /// <summary>
        /// 阿里云盘 - 创建文件夹
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="parentFileId"></param>
        /// <param name="name"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public AliyunDriveFileItem CreateFolder(string driveId, string parentFileId, string name, string accessToken)
        {
            // v1 https://openapi.alipan.com/adrive/v1.0/openFile/create
            // v2 https://api.aliyundrive.com/adrive/v2/file/createWithFolders
            var request = new RestRequest("/adrive/v1.0/openFile/create", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {accessToken}");

            var body = new
            {
                drive_id = driveId,
                parent_file_id = parentFileId,
                name = name,
                type = "folder",
                check_name_mode = "refuse", // 同名不创建
            };

            request.AddBody(body);
            var response = WithRetry<AliyunDriveFileItem>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response.Data;
            }

            throw response.ErrorException ?? new Exception(response.Content ?? "创建文件夹失败");
        }

        /// <summary>
        /// 阿里云盘 - 执行重试请求
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public RestResponse<T> WithRetry<T>(RestRequest request)
        {
            const int maxRetries = 5;
            int retries = 0;
            while (true)
            {
                var response = _apiClient.Execute<T>(request);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return response;
                }
                else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    retries++;

                    // 其他API加一起有个10秒150次的限制。
                    // 可以根据429和 x-retry - after 头部来判断等待重试的时间

                    _log.Warning("触发限流，请求次数过多，重试第 {@0} 次： {@1}", retries, request.Resource);

                    if (retries >= maxRetries)
                    {
                        throw new Exception("请求次数过多，已达到最大重试次数");
                    }

                    var waitTime = response.Headers.FirstOrDefault(x => x.Name == "x-retry-after")?.Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(waitTime) && int.TryParse(waitTime, out var waitValue) && waitValue > REQUEST_INTERVAL)
                    {
                        //await Task.Delay(waitValue);
                        Thread.Sleep(waitValue);
                    }
                    else
                    {
                        //await Task.Delay(REQUEST_INTERVAL);
                        Thread.Sleep(REQUEST_INTERVAL);
                    }
                }
                else if ((response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Conflict) && response.Content.Contains("PreHashMatched"))
                {
                    // 如果是秒传预处理的错误码，则直接返回
                    return response;
                }
                else
                {
                    _log.Error(response.ErrorException, $"请求失败：{request.Resource} {response.StatusCode} {response.Content}");

                    throw response.ErrorException ?? new Exception($"请求失败：{response.StatusCode} {response.Content}");
                }
            }
        }
    }
}