using MDriveSync.Core;
using Microsoft.AspNetCore.Mvc;

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
        public async Task<AliyunDriveFileItem> GetDetail(string jobId, string fileId)
        {
            var jobs = _timedHostedService.GetJobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                return await job.AliyunDriveGetDetail(fileId);
            }
            return null;
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