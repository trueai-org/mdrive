using MDriveSync.Core;
using MDriveSync.Core.IO;
using MDriveSync.Core.ViewModels;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

namespace MDriveSync.Client.API.Controllers
{
    /// <summary>
    /// 云盘作业控制器
    /// </summary>
    [Route("api/drive")]
    [ApiController]
    public class DriveController : ControllerBase
    {
        private readonly TimedHostedService _timedHostedService;

        public DriveController(TimedHostedService timedHostedService)
        {
            _timedHostedService = timedHostedService;
        }

        /// <summary>
        /// 常用的表达式
        /// </summary>
        /// <returns></returns>
        [HttpGet("crons")]
        public List<string> GetExpressions()
        {
            return QuartzCronScheduler.CommonExpressions.Keys.ToList();
        }

        /// <summary>
        /// 获取云盘配置
        /// </summary>
        /// <returns></returns>
        [HttpGet("drives")]
        public List<AliyunDriveConfig> GetDrives()
        {
            return _timedHostedService.Drives();
        }

        /// <summary>
        /// 获取云盘文件文件夹
        /// </summary>
        /// <param name="jobId"></param>
        /// <returns></returns>
        [HttpGet("files/{jobId}")]
        public List<AliyunDriveFileItem> GetDrivleFiles(string jobId, [FromQuery] string parentId = "")
        {
            var jobs = _timedHostedService.Jobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                return job.GetDrivleFiles(parentId);
            }
            return new List<AliyunDriveFileItem>();
        }

        /// <summary>
        /// 获取云盘文件下载链接
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="fileId"></param>
        /// <returns></returns>
        [HttpGet("download/{jobId}/{fileId}")]
        public async Task<AliyunDriveOpenFileGetDownloadUrlResponse> GetDownloadUrl(string jobId, string fileId)
        {
            var jobs = _timedHostedService.Jobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                return await job.AliyunDriveGetDownloadUrl(fileId);
            }
            return null;
        }

        /// <summary>
        /// 获取文件详情
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="fileId"></param>
        /// <returns></returns>
        [HttpGet("file/{jobId}/{fileId}")]
        public async Task<FilePathKeyResult> GetDetail(string jobId, string fileId)
        {
            var jobs = _timedHostedService.Jobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                return await job.GetFileDetail(fileId);
            }
            return null;
        }

        /// <summary>
        /// 云盘添加
        /// </summary>
        /// <param name="cfg"></param>
        /// <returns></returns>
        [HttpPost()]
        public Result DriveAdd([FromBody] RefreshTokenRequest cfg)
        {
            _timedHostedService.DriveAdd(cfg);
            return Result.Ok();
        }

        /// <summary>
        /// 云盘编辑
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="cfg"></param>
        /// <returns></returns>
        [HttpPut("{driveId}")]
        public Result DriveEdit(string driveId, [FromBody] RefreshTokenRequest cfg)
        {
            _timedHostedService.DriveEdit(driveId, cfg);
            return Result.Ok();
        }

        /// <summary>
        /// 云盘删除
        /// </summary>
        /// <param name="driveId"></param>
        /// <returns></returns>
        [HttpDelete("{driveId}")]
        public Result DriveDelete(string driveId)
        {
            _timedHostedService.DriveDelete(driveId);
            return Result.Ok();
        }

        /// <summary>
        /// 更新作业配置（只有空闲、错误、取消、禁用、完成状态才可以更新）
        /// </summary>
        /// <param name="cfg"></param>
        [HttpPut("job")]
        public Result JobUpdate([FromBody] JobConfig cfg)
        {
            var jobs = _timedHostedService.Jobs();
            if (jobs.TryGetValue(cfg.Id, out var job) && job != null)
            {
                job.JobUpdate(cfg);
            }
            return Result.Ok();
        }

        /// <summary>
        /// 作业添加
        /// </summary>
        /// <param name="cfg"></param>
        /// <returns></returns>
        [HttpPost("job/{driveId}")]
        public Result JobAdd(string driveId, [FromBody] JobConfig cfg)
        {
            _timedHostedService.JobDelete(driveId, cfg);
            return Result.Ok();
        }

        /// <summary>
        /// 作业状态修改
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        [HttpPut("job/{jobId}/{state}")]
        public Result JobStateChange(string jobId, JobState state)
        {
            var jobs = _timedHostedService.Jobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                job.JobStateChange(state);

                // 删除作业，清除服务
                if (state == JobState.Deleted)
                {
                    _timedHostedService.JobDelete(jobId);
                }
            }
            return Result.Ok();
        }

        /// <summary>
        /// 文件下载
        /// 直接流式传输的方法是一种高效处理大文件下载的方式。
        /// 这种方法通过直接将原始响应流传输到客户端，避免了将文件内容完全加载到服务器内存中。这样做既减少了内存消耗，也提高了处理大文件的效率。
        /// </summary>
        /// <param name="url"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        [HttpGet("download")]
        public async Task<IActionResult> DownloadFileAsync([FromQuery] string url, [FromQuery] string name)
        {
            // 使用 HttpClient 发送请求
            var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                return BadRequest("无法下载文件");
            }

            // 直接从 HttpResponseMessage 获取流
            var stream = await response.Content.ReadAsStreamAsync();

            // 设置为分块传输
            //Response.Headers.Append("Transfer-Encoding", "chunked");

            // 设置文件名和内容类型
            var contentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = name };
            Response.Headers.Append("Content-Disposition", contentDisposition.ToString());

            var mimeType = "application/octet-stream";
            //var extension = Path.GetExtension(name).ToLowerInvariant();
            //switch (extension)
            //{
            //    case ".mp3":
            //        mimeType = "audio/mpeg";
            //        break;
            //    case ".mp4":
            //        mimeType = "video/mp4";
            //        break;
            //    case ".jpg":
            //    case ".jpeg":
            //        mimeType = "image/jpeg";
            //        break;
            //    case ".png":
            //        mimeType = "image/png";
            //        break;
            //    default:
            //        break;
            //}

            // 返回 FileStreamResult，使用原始流
            return new FileStreamResult(stream, mimeType);
        }

        ///// <summary>
        ///// 文件下载
        ///// </summary>
        ///// <param name="fileUrl"></param>
        ///// <returns></returns>
        //[HttpGet("download")]
        //public async Task<IActionResult> DownloadFile([FromQuery] string url, [FromQuery] string name)
        //{
        //    using (var httpClient = new HttpClient())
        //    {
        //        var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        //        if (!response.IsSuccessStatusCode)
        //        {
        //            return BadRequest("无法下载文件");
        //        }

        //        var stream = await response.Content.ReadAsStreamAsync();
        //        var contentDisposition = new ContentDispositionHeaderValue("attachment")
        //        {
        //            FileName = name
        //        };

        //        Response.Headers.Append("Content-Disposition", contentDisposition.ToString());
        //        return File(stream, "application/octet-stream");
        //    }
        //}

        ///// <summary>
        ///// 使用分块传输和流式处理来优化大文件下载
        ///// </summary>
        ///// <param name="url"></param>
        ///// <param name="name"></param>
        ///// <returns></returns>
        //[HttpGet("download")]
        //public async Task<IActionResult> DownloadFileAsync([FromQuery] string url, [FromQuery] string name)
        //{
        //    var httpClient = new HttpClient();
        //    var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        //    if (!response.IsSuccessStatusCode)
        //    {
        //        return BadRequest("无法下载文件");
        //    }

        //    var stream = await response.Content.ReadAsStreamAsync();

        //    // 设置为分块传输
        //    Response.Headers.Append("Transfer-Encoding", "chunked");
        //    var contentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = name };
        //    Response.Headers.Append("Content-Disposition", contentDisposition.ToString());

        //    return new FileStreamResult(stream, "application/octet-stream");
        //}

        /// <summary>
        /// 获取文价夹/路径下拉列表
        /// </summary>
        /// <returns></returns>
        [HttpPost("paths")]
        public Result<List<TreeNode>> GetPaths([FromBody] TreeNodePathRequest request)
        {
            var data = Filesystem.TreeNodes(request.Path);
            return Result.Ok(data);
        }
    }
}