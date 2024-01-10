using MDriveSync.Core.DB;
using MDriveSync.Core.ViewModels;
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
        //private readonly ConcurrentDictionary<string, MountDrive> _cloudDrives = new();

        private readonly ILogger _logger;
        private readonly ClientOptions _clientOptions;
        private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
        private readonly ConcurrentDictionary<string, Job> _jobs = new();

        private Timer _timer;

        public TimedHostedService(
            ILogger<TimedHostedService> logger,
            IOptionsMonitor<ClientOptions> clientOptions)
        {
            _logger = logger;
            _clientOptions = clientOptions?.CurrentValue;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            stoppingToken.Register(() => _logger.LogDebug($"例行检查服务已停止"));

            // 启动时，如果有配置云盘，则新增到数据库
            // 如果数据库已有，则跳过
            if (_clientOptions?.AliyunDrives?.Count > 0)
            {
                var drives = DriveDb.Instacne.GetAll();
                foreach (var cd in _clientOptions?.AliyunDrives)
                {
                    var f = drives.FirstOrDefault(x => x.Id == cd.Id);
                    if (f == null)
                    {
                        DriveDb.Instacne.Add(cd);
                    }

                    //else
                    //{
                    //    f = cd;
                    //    DriveDb.Instacne.Update(f);
                    //}
                }
            }

            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

            return Task.CompletedTask;
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

                var ds = DriveDb.Instacne.GetAll();

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

                        if (job.CurrentState != JobState.Disabled)
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

        /// <summary>
        /// 作业列表
        /// </summary>
        /// <returns></returns>
        public ConcurrentDictionary<string, Job> Jobs()
        {
            return _jobs;
        }

        /// <summary>
        /// 添加作业
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="cfg"></param>
        /// <exception cref="LogicException"></exception>
        public void JobAdd(string driveId, JobConfig cfg)
        {
            var drives = DriveDb.Instacne.GetAll();
            var drive = drives.Where(c => c.Id == driveId).FirstOrDefault();
            if (drive == null)
            {
                throw new LogicException("云盘不存在");
            }

            // 默认禁用状态
            cfg.State = JobState.Disabled;
            cfg.Id = Guid.NewGuid().ToString("N");

            drive.Jobs ??= new List<JobConfig>();
            drive.Jobs.Add(cfg);

            // 持久化
            drive.Save();

            // 添加到队列
            if (!_jobs.TryGetValue(cfg.Id, out var job) || job == null)
            {
                job = new Job(drive, cfg, _logger);
                _jobs[cfg.Id] = job;
            }
        }

        /// <summary>
        /// 删除作业
        /// </summary>
        /// <param name="jobId"></param>
        public void JobDelete(string jobId)
        {
            _jobs.TryRemove(jobId, out _);
        }

        /// <summary>
        /// 云盘列表
        /// </summary>
        /// <returns></returns>
        public List<AliyunDriveConfig> Drives()
        {
            var jobs = Jobs();

            var ds = DriveDb.Instacne.GetAll();
            foreach (var kvp in ds)
            {
                var js = kvp.Jobs.ToList();
                js.ForEach(j =>
                {
                    if (jobs.TryGetValue(j.Id, out var job))
                    {
                        //j = job.CurrrentJob.GetClone();
                        j.State = job.CurrentState;
                    }
                });
                kvp.Jobs = js;
            }

            return ds;
        }

        /// <summary>
        /// 添加云盘
        /// </summary>
        public void DriveAdd(RefreshTokenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.RefreshToken))
            {
                throw new LogicParamException();
            }

            var drive = new AliyunDriveConfig()
            {
                Id = Guid.NewGuid().ToString("N"),
                RefreshToken = request.RefreshToken,
                Jobs = []
            };

            // 保存配置
            drive.Save();
        }

        /// <summary>
        /// 编辑云盘
        /// </summary>
        public void DriveEdit(string driveId, RefreshTokenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.RefreshToken))
            {
                throw new LogicParamException();
            }

            var drive = DriveDb.Instacne.Get(driveId);
            if (drive == null)
            {
                throw new LogicException("云盘不存在");
            }

            drive.RefreshToken = request.RefreshToken;

            // 保存配置
            drive.Save();
        }

        /// <summary>
        /// 删除云盘
        /// </summary>
        public void DriveDelete(string driveId)
        {
            var drive = DriveDb.Instacne.Get(driveId);
            if (drive == null)
            {
                throw new LogicException("云盘不存在");
            }

            // 清除作业
            foreach (var j in drive.Jobs)
            {
                JobDelete(j.Id);
            }

            // 保存配置
            drive.Save(true);
        }

        /// <summary>
        /// 挂载磁盘
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="mountPoint"></param>
        public void DriveMount(string jobId, string mountPoint)
        {
            if (_jobs.TryGetValue(jobId, out var job) || job != null)
            {
                job.DriveMount(mountPoint);
            }

            //var cloudDrive = new MountDrive(mountPoint);
            //cloudDrive.Mount();
            //_cloudDrives.TryAdd(mountPoint, cloudDrive);

            //try
            //{
            //var dokanLogger = new ConsoleLogger("[Dokan] ");

            //// 创建 Dokan 实例
            //var dokan = new Dokan(dokanLogger);

            //// 使用 DokanInstanceBuilder 创建 Dokan 实例
            //var dokanInstanceBuilder = new DokanInstanceBuilder(dokan)
            //    .ConfigureOptions(options =>
            //    {
            //        options.Options = DokanOptions.DebugMode | DokanOptions.EnableNotificationAPI;
            //        options.MountPoint = mountPoint;
            //    });

            //using (var dokanInstance = dokanInstanceBuilder.Build(cloudDrive))
            //{
            //    //Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs e) =>
            //    //{
            //    //    e.Cancel = true;
            //    //    //Dokan.RemoveMountPoint(mountPoint);
            //    //};

            //    await dokanInstance.WaitForFileSystemClosedAsync(uint.MaxValue);
            //}

            //Console.WriteLine("云盘已卸载。");
            //}
            //catch (DokanException ex)
            //{
            //    Console.WriteLine("发生错误: " + ex.Message);
            //}
        }

        /// <summary>
        /// 卸载磁盘挂载
        /// </summary>
        /// <param name="jobId"></param>
        public void DriveUnmount(string jobId)
        {
            if (_jobs.TryGetValue(jobId, out var job) || job != null)
            {
                job.DriveUnmount();
            }

            //if (_cloudDrives.TryRemove(mountPoint, out var mountDrive))
            //{
            //    mountDrive.Unmount();
            //}
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
    }
}