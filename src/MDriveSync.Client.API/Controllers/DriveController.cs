﻿using MDriveSync.Core;
using MDriveSync.Core.DB;
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
        /// 挂载磁盘 - 云盘挂载
        /// </summary>
        /// <returns></returns>
        [HttpPost("mount/{driveId}")]
        public Result DriveMount(string driveId)
        {
            _timedHostedService.DriveMount(driveId);
            return Result.Ok();
        }

        /// <summary>
        /// 挂载磁盘 - 云盘卸载
        /// </summary>
        /// <param name="driveId"></param>
        /// <returns></returns>
        [HttpPost("unmount/{driveId}")]
        public Result DriveUnmount(string driveId)
        {
            _timedHostedService.DriveUnmount(driveId);
            return Result.Ok();
        }

        /// <summary>
        /// 挂载磁盘 - 云盘作业挂载
        /// </summary>
        /// <param name="mountRequest"></param>
        /// <returns></returns>
        [HttpPost("job/mount/{jobId}")]
        public Result DriveJobMount(string jobId)
        {
            _timedHostedService.DriveJobMount(jobId);
            return Result.Ok();
        }

        /// <summary>
        /// 挂载磁盘 - 云盘作业卸载
        /// </summary>
        /// <param name="driveId"></param>
        /// <returns></returns>
        [HttpPost("job/unmount/{jobId}")]
        public Result DriveJobUnmount(string jobId)
        {
            _timedHostedService.DriveJobUnmount(jobId);
            return Result.Ok();
        }

        /// <summary>
        /// 获取作业
        /// </summary>
        /// <returns></returns>
        [HttpGet("jobs")]
        public Result<List<JobConfig>> GetJobs()
        {
            var data = _timedHostedService.Jobs().Values
                .ToList()
                .Select(c =>
                {
                    var j = c.CurrrentJob.GetClone();
                    j.State = c.CurrentState;
                    j.Metadata ??= new();
                    j.Metadata.Message = c.ProcessMessage;
                    j.IsMount = c.DriveIsMount();

                    return j;
                }).ToList();
            return Result.Ok(data);
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
        public AliyunDriveOpenFileGetDownloadUrlResponse GetDownloadUrl(string jobId, string fileId)
        {
            var jobs = _timedHostedService.Jobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                return job.AliyunDriveGetDownloadUrl(fileId);
            }
            return null;
        }

        /// <summary>
        /// 下载文件V2 - 支持加密文件下载（边下载边解压/解密）
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="fileId"></param>
        /// <returns></returns>
        [HttpGet("download-v2/{jobId}/{fileId}")]
        public async Task<IActionResult> DownloadV2(string jobId, string fileId)
        {
            var jobs = _timedHostedService.Jobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                var url = job.AliyunDriveGetDownloadUrl(fileId);

                // TODO
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
        public FilePathKeyResult GetDetail(string jobId, string fileId)
        {
            var jobs = _timedHostedService.Jobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                return job.GetFileDetail(fileId);
            }
            return null;
        }

        /// <summary>
        /// 云盘添加
        /// </summary>
        /// <param name="cfg"></param>
        /// <returns></returns>
        [HttpPost()]
        public Result DriveAdd([FromBody] DriveEditRequest cfg)
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
        public Result DriveEdit(string driveId, [FromBody] DriveEditRequest cfg)
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
            _timedHostedService.JobAdd(driveId, cfg);
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
            }

            // 如果作业不再执行队列中
            if (state == JobState.Deleted)
            {
                // 删除作业，清除服务
                _timedHostedService.JobDelete(jobId);

                // 修复 https://github.com/trueai-org/MDriveSync/issues/4
                var ds = DriveDb.Instacne.GetAll();
                foreach (var d in ds)
                {
                    var jo = d?.Jobs?.FirstOrDefault(x => x.Id == jobId);
                    if (jo != null)
                    {
                        d.Jobs.RemoveAll(x => x.Id == jobId);
                        DriveDb.Instacne.Update(d);
                    }
                }
            }

            return Result.Ok();
        }

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

        /// <summary>
        /// 获取可用于挂载的磁盘盘符或挂载点
        /// </summary>
        /// <returns></returns>
        [HttpGet("points")]
        public Result<List<string>> GetAvailableMountPoints()
        {
            var data = Filesystem.GetAvailableMountPoints().ToList();
            return Result.Ok(data);
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
    }
}