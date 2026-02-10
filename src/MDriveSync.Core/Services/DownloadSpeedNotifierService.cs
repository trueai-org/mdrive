using MDriveSync.Core.Hubs;
using MDriveSync.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 下载速度实时推送后台服务。
    /// 当存在活跃的下载任务时，每秒通过 SignalR 向前端推送全局下载速度和任务状态。
    /// </summary>
    public class DownloadSpeedNotifierService : BackgroundService
    {
        private readonly IHubContext<JobHub> _hubContext;
        private readonly ILogger<DownloadSpeedNotifierService> _logger;

        /// <summary>
        /// 上一次任务状态快照，用于检测变化
        /// key: taskId, value: status
        /// </summary>
        private Dictionary<string, DownloadStatus> _previousTaskStates = new();

        public DownloadSpeedNotifierService(
            IHubContext<JobHub> hubContext,
            ILogger<DownloadSpeedNotifierService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // 最多只等待 1s
                var sw = Stopwatch.StartNew();
                try
                {
                    var tasks = DownloadManager.Instance.GetDownloadTasks();
                    var hasActiveTask = tasks.Any(t =>
                        t.Status == DownloadStatus.Downloading ||
                        t.Status == DownloadStatus.Pending);

                    // 检测任务状态是否发生变化（新增、删除、状态变更）
                    var currentStates = tasks.ToDictionary(t => t.Id, t => t.Status);
                    var tasksChanged = HasTasksChanged(currentStates);
                    _previousTaskStates = currentStates;

                    // 如果任务数量变化，也要通知，因为可能变为 0
                    if (hasActiveTask || tasksChanged)
                    {
                        var currentSpeed = DownloadManager.Instance.GetGlobalDownloadSpeed();
                        var speedDisplay = currentSpeed.ToFileSize() + "/s";

                        // 当全局下载速度变化时推送
                        await _hubContext.Clients.All.SendAsync("DownloadSpeedChanged", new
                        {
                            speed = currentSpeed,        // double，字节/秒
                            speedString = speedDisplay   // string，如 "1.23 MB/s"
                        }, stoppingToken);
                    }

                    // 当下载任务状态发生变化时推送
                    // 或进度发生变化时也推送（即使没有活跃任务，也要通知前端更新界面）
                    if (tasksChanged || hasActiveTask)
                    {
                        await _hubContext.Clients.All.SendAsync("DownloadTasksChanged", tasks, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 服务停止，正常退出
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "推送下载速度时发生异常");
                }

                sw.Stop();

                var elapsed = sw.ElapsedMilliseconds;
                if (elapsed < 1000)
                {
                    await Task.Delay(1000 - (int)elapsed, stoppingToken);
                }
            }
        }

        /// <summary>
        /// 检测任务状态是否发生变化（新增、删除、状态变更）
        /// </summary>
        private bool HasTasksChanged(Dictionary<string, DownloadStatus> currentStates)
        {
            if (currentStates.Count != _previousTaskStates.Count)
            {
                return true;
            }

            foreach (var (id, status) in currentStates)
            {
                if (!_previousTaskStates.TryGetValue(id, out var prevStatus) || prevStatus != status)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
