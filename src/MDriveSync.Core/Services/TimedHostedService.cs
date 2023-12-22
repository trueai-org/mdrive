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
            stoppingToken.Register(() => _logger.LogDebug($"服务已停止"));

            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
            return Task.CompletedTask;
        }

        private async void DoWork(object state)
        {
            if (_semaphoreSlim.CurrentCount == 0)
            {
                //_logger.LogInformation("执行中...");
                return;
            }

            await _semaphoreSlim.WaitAsync();

            try
            {
                _logger.LogInformation("开始作业");

                foreach (var ad in _clientOptions.CurrentValue.AliyunDrives)
                {
                    foreach (var cf in ad.Jobs)
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

                _logger.LogInformation("执行完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行任务时发生错误");
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("服务已停止");

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
    }
}