using MDriveSync.Core;
using MDriveSync.Core.DB;
using MDriveSync.Core.Models;
using MDriveSync.Core.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace MDriveSync.Client.API.Controllers
{
    /// <summary>
    /// 本地存储控制器
    /// </summary>
    [Route("api/local")]
    [ApiController]
    public class LocalStorageController : ControllerBase
    {
        private readonly LocalStorageHostedService _localStorageHostedService;

        public LocalStorageController(LocalStorageHostedService localStorageHostedService)
        {
            _localStorageHostedService = localStorageHostedService;
        }

        /// <summary>
        /// 获取工作组
        /// </summary>
        /// <returns></returns>
        [HttpGet("storages")]
        public Result<List<LocalStorageConfig>> Storages()
        {
            var data = _localStorageHostedService.Storages();
            return Result.Ok(data);
        }

        /// <summary>
        /// 获取工作组作业
        /// </summary>
        /// <returns></returns>
        [HttpGet("jobs")]
        public Result<List<LocalJobConfig>> GetJobs()
        {
            var data = _localStorageHostedService.Jobs().Values
                .ToList()
                .Select(c =>
                {
                    var j = c.CurrrentJob.GetClone<LocalJobConfig>();

                    j.State = c.CurrentState;
                    j.Metadata ??= new();
                    j.Metadata.Message = c.ProcessMessage;
            
                    return j;
                }).ToList();
            return Result.Ok(data);
        }

        /// <summary>
        /// 添加工作组
        /// </summary>
        /// <param name="cfg"></param>
        /// <returns></returns>
        [HttpPost()]
        public Result StorageAdd([FromBody] LocalStorageEditRequest cfg)
        {
            _localStorageHostedService.DriveAdd(cfg.Name);
            return Result.Ok();
        }

        /// <summary>
        /// 编辑工作组
        /// </summary>
        /// <param name="storageId"></param>
        /// <param name="cfg"></param>
        /// <returns></returns>
        [HttpPut("{storageId}")]
        public Result StorageEdit(string storageId, [FromBody] LocalStorageEditRequest cfg)
        {
            _localStorageHostedService.DriveEdit(storageId, cfg.Name);
            return Result.Ok();
        }

        /// <summary>
        /// 删除工作组
        /// </summary>
        /// <param name="storageId"></param>
        /// <returns></returns>
        [HttpDelete("{storageId}")]
        public Result StorageDelete(string storageId)
        {
            _localStorageHostedService.DriveDelete(storageId);
            return Result.Ok();
        }

        /// <summary>
        /// 更新作业配置（只有空闲、错误、取消、禁用、完成状态才可以更新）
        /// </summary>
        /// <param name="cfg"></param>
        [HttpPut("job")]
        public Result JobUpdate([FromBody] LocalJobConfig cfg)
        {
            var jobs = _localStorageHostedService.Jobs();
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
        [HttpPost("job/{storageId}")]
        public Result JobAdd(string storageId, [FromBody] LocalJobConfig cfg)
        {
            _localStorageHostedService.JobAdd(storageId, cfg);
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
            var jobs = _localStorageHostedService.Jobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                job.JobStateChange(state);
            }

            // 如果作业不再执行队列中
            if (state == JobState.Deleted)
            {
                // 删除作业，清除服务
                _localStorageHostedService.JobDelete(jobId);

                var ds = AliyunStorageDb.Instance.DB.GetAll(false);
                foreach (var d in ds)
                {
                    var jo = d?.Jobs?.FirstOrDefault(x => x.Id == jobId);
                    if (jo != null)
                    {
                        d.Jobs.RemoveAll(x => x.Id == jobId);
                        AliyunStorageDb.Instance.DB.Update(d);
                    }
                }
            }

            return Result.Ok();
        }
    }
}