using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MDriveSync.Core
{
    /// <summary>
    /// 定时检查服务
    /// 定时服务就可以在后台运行，且不会影响应用程序的启动和发布过程
    /// </summary>
    public class TimedHostedService : BackgroundService, IDisposable
    {
        //private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<TimedHostedService> _logger;

        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
        private readonly IOptionsMonitor<ClientOptions> _clientOptions;

        private Timer _timer;
        private readonly ConcurrentDictionary<string, Job> _jobs = new ConcurrentDictionary<string, Job>();

        public TimedHostedService(
            //IServiceScopeFactory serviceScopeFactory,
            ILogger<TimedHostedService> logger,
            IOptionsMonitor<ClientOptions> clientOptions)
        {
            //_serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _clientOptions = clientOptions;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            stoppingToken.Register(() => _logger.LogDebug($"例行检查服务已停止"));

            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
            return Task.CompletedTask;
        }



        //private void DoWork(object state)
        //{
        //    // 加锁，以防万一重复执行
        //    lock (_lock)
        //    {
        //        // 重新设定定时器，防止在当前工作完成前触发下一次执行
        //        _timer?.Change(Timeout.Infinite, 0);

        //        try
        //        {
        //            _logger.LogInformation("刷新图片、视频大小服务开始工作.");
        //            using (var scope = _serviceScopeFactory.CreateScope())
        //            {
        //                var mediaService = scope.ServiceProvider.GetRequiredService<IMediaService>();
        //                await mediaService.RefreshMetaJob();
        //            }
        //            // 执行刷新图片、视频大小服务
        //            var task = Task.Run(async () => await _mediaService.RefreshMetaJob());
        //            task.Wait();
        //        }
        //        finally
        //        {
        //            // 任务完成后重新启动定时器
        //            _timer?.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        //        }
        //    }
        //}

        private async void DoWork(object state)
        {
            if (_semaphoreSlim.CurrentCount == 0)
            {
                return;
            }

            await _semaphoreSlim.WaitAsync();

            try
            {
                _logger.LogInformation("开始例行检查");

                var ds = _clientOptions.CurrentValue.AliyunDrives.ToList();
                foreach (var ad in ds)
                {
                    var jobs = ad.Jobs.ToList();
                    foreach (var cf in jobs)
                    {
                        if (!_jobs.TryGetValue(cf.Id, out var job) || job == null)
                        {
                            job = new Job(ad, cf, _logger);
                            _jobs[cf.Id] = job;
                        }

                        if (job.State != JobState.Disabled)
                        {
                            await job.Maintenance();
                        }
                    }
                }

                GC.Collect();

                _logger.LogInformation("例行检查完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行例行检查时发生异常");
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("例行检查服务已停止");

            _timer?.Change(Timeout.Infinite, 0);
            await _semaphoreSlim.WaitAsync();
            _semaphoreSlim.Release();
            await base.StopAsync(stoppingToken);
        }

        public override void Dispose()
        {
            _timer?.Dispose();
            _semaphoreSlim.Dispose();
            base.Dispose();
        }

        /// <summary>
        /// 获取作业
        /// </summary>
        /// <returns></returns>
        public ConcurrentDictionary<string, Job> GetJobs()
        {
            return _jobs;
        }

        /// <summary>
        /// 获取所有云盘
        /// </summary>
        /// <returns></returns>
        public List<AliyunDriveConfig> GetDrives()
        {
            var jobs = GetJobs();

            var ds = _clientOptions.CurrentValue.AliyunDrives;
            foreach (var kvp in ds)
            {
                foreach (var j in kvp.Jobs)
                {
                    if (jobs.TryGetValue(j.Id, out var job))
                    {
                        j.State = job.State;
                    }
                }
            }

            return ds;
        }
    }
}