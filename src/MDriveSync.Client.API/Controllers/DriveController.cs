using MDriveSync.Core;
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
        /// 获取云盘配置
        /// </summary>
        /// <returns></returns>
        [HttpGet("drives")]
        public List<AliyunDriveConfig> GetDrives()
        {
            return _timedHostedService.GetDrives();
        }

        /// <summary>
        /// 获取云盘文件文件夹
        /// </summary>
        /// <param name="jobId"></param>
        /// <returns></returns>
        [HttpGet("files/{jobId}")]
        public List<AliyunDriveFileItem> GetDrivleFiles(string jobId, [FromQuery] string parentId = "")
        {
            var jobs = _timedHostedService.GetJobs();
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
            var jobs = _timedHostedService.GetJobs();
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
        public async Task<FilePathDetailResult> GetDetail(string jobId, string fileId)
        {
            var jobs = _timedHostedService.GetJobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                return await job.GetFileDetail(fileId);
            }
            return null;
        }

        /// <summary>
        /// 文件下载
        /// </summary>
        /// <param name="fileUrl"></param>
        /// <returns></returns>
        [HttpGet("download")]
        public async Task<IActionResult> DownloadFile([FromQuery] string url, [FromQuery] string name)
        {
            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    return BadRequest("无法下载文件");
                }

                var stream = await response.Content.ReadAsStreamAsync();
                var contentDisposition = new ContentDispositionHeaderValue("attachment")
                {
                    FileName = name
                };

                Response.Headers.Append("Content-Disposition", contentDisposition.ToString());
                return File(stream, "application/octet-stream");
            }
        }

        // POST api/<JobController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<JobController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<JobController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}