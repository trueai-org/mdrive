using MDriveSync.Core.DB;
using MDriveSync.Core.IO;
using MDriveSync.Core.Models;
using MDriveSync.Core.Services;
using MDriveSync.Core.ViewModels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace MDriveSync.Core
{
    /// <summary>
    /// 定时检查服务
    /// 定时服务就可以在后台运行，且不会影响应用程序的启动和发布过程
    /// </summary>
    public class AliyunDriveHostedService : BackgroundService, IDisposable
    {
        //private readonly IServiceScopeFactory _serviceScopeFactory;
        //private readonly ConcurrentDictionary<string, MountDrive> _cloudDrives = new();

        private readonly ILogger _logger;
        private readonly ClientOptions _clientOptions;
        private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

        // 云盘作业
        private readonly ConcurrentDictionary<string, AliyunJob> _jobs = new();

        // 云盘挂载
        private readonly ConcurrentDictionary<string, AliyunDriveMounter> _mounter = new();

        private Timer _timer;

        public AliyunDriveHostedService(
            ILogger<AliyunDriveHostedService> logger,
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
                InitAddJob(_clientOptions.AliyunDrives);
            }

            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

            return Task.CompletedTask;
        }

        /// <summary>
        /// 初始化新增作业
        /// </summary>
        public void InitAddJob(List<AliyunStorageConfig> aliyunDriveConfigs)
        {
            var drives = AliyunStorageDb.Instance.DB.GetAll();
            foreach (var cd in aliyunDriveConfigs)
            {
                var f = drives.FirstOrDefault(x => x.Id == cd.Id);
                if (f == null)
                {
                    AliyunStorageDb.Instance.DB.Add(cd);
                }

                //else
                //{
                //    f = cd;
                //    DriveDb.Instacne.Update(f);
                //}
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

                var ds = AliyunStorageDb.Instance.DB.GetAll();

                foreach (var ad in ds)
                {
                    // 云盘自动挂载
                    if (Platform.IsClientWindows)
                    {
                        try
                        {
                            if (ad?.MountConfig?.MountOnStartup == true && !string.IsNullOrWhiteSpace(ad?.MountConfig?.MountPoint))
                            {
                                if (!_mounter.TryGetValue(ad.Id, out var mt) || mt == null)
                                {
                                    mt = new AliyunDriveMounter(ad, ad.MountConfig);

                                    //mt.AliyunDriveInitFiles();

                                    mt.Mount();
                                    _mounter[ad.Id] = mt;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "云盘挂载异常");
                        }
                    }

                    // 云盘作业
                    var jobs = ad.Jobs.ToList();
                    foreach (var cf in jobs)
                    {
                        if (!_jobs.TryGetValue(cf.Id, out var job) || job == null)
                        {
                            job = new AliyunJob(ad, cf, _logger);
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
        public ConcurrentDictionary<string, AliyunJob> Jobs()
        {
            return _jobs;
        }

        /// <summary>
        /// 添加作业
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="cfg"></param>
        /// <exception cref="LogicException"></exception>
        public void JobAdd(string driveId, AliyunJobConfig cfg)
        {
            var drives = AliyunStorageDb.Instance.DB.GetAll();
            var drive = drives.Where(c => c.Id == driveId).FirstOrDefault();
            if (drive == null)
            {
                throw new LogicException("云盘不存在");
            }

            // 加密配置验证
            if (cfg.IsEncrypt)
            {
                if (string.IsNullOrEmpty(cfg.HashAlgorithm) || string.IsNullOrEmpty(cfg.EncryptAlgorithm) || string.IsNullOrEmpty(cfg.EncryptKey))
                {
                    throw new LogicException("加密配置不完整");
                }

                // 压缩算法验证
                if (!string.IsNullOrWhiteSpace(cfg.CompressAlgorithm))
                {
                    var allowComs = new[] { "Zstd", "LZ4", "Snappy" };
                    if (!allowComs.Contains(cfg.CompressAlgorithm))
                    {
                        throw new LogicException("压缩算法不支持");
                    }
                }

                // 加密算法验证
                var allowEncrypts = new[] { "AES256-GCM", "ChaCha20-Poly1305" };
                if (!allowEncrypts.Contains(cfg.EncryptAlgorithm))
                {
                    throw new LogicException("加密算法不支持");
                }

                // 哈希算法验证
                var allowHashs = new[] { "SHA256", "BLAKE3" };
                if (!allowHashs.Contains(cfg.HashAlgorithm))
                {
                    throw new LogicException("哈希算法不支持");
                }
            }
            else
            {
                cfg.IsEncryptName = false;
                cfg.HashAlgorithm = null;
                cfg.EncryptAlgorithm = null;
                cfg.CompressAlgorithm = null;
                cfg.EncryptKey = null;
            }

            // 默认禁用状态
            cfg.State = JobState.Disabled;
            cfg.Id = Guid.NewGuid().ToString("N");

            drive.Jobs ??= new List<AliyunJobConfig>();

            // 禁止作业指向同一目标
            if (!string.IsNullOrWhiteSpace(cfg.Target) && drive.Jobs.Any(x => x.Target == cfg.Target))
            {
                throw new LogicException("多个作业禁止指向云盘同一个目标目录");
            }

            drive.Jobs.Add(cfg);

            // 持久化
            drive.Save();

            // 添加到队列
            if (!_jobs.TryGetValue(cfg.Id, out var job) || job == null)
            {
                job = new AliyunJob(drive, cfg, _logger);
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
        public List<AliyunStorageConfig> Drives()
        {
            var jobs = Jobs();

            var ds = AliyunStorageDb.Instance.DB.GetAll();
            foreach (var kvp in ds)
            {
                // 是否挂载
                kvp.IsMount = _mounter.ContainsKey(kvp.Id);

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
        public void DriveAdd(AliyunDriveEditRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.RefreshToken))
            {
                throw new LogicParamException();
            }

            var drive = new AliyunStorageConfig()
            {
                Id = Guid.NewGuid().ToString("N"),
                RefreshToken = request.RefreshToken,
                Jobs = [],
            };
            drive.MountConfig = new AliyunDriveMountConfig()
            {
                IsRecycleBin = request.IsRecycleBin,
                MountDrive = request.MountDrive,
                MountOnStartup = request.MountOnStartup,
                MountPath = request.MountPath,
                MountPoint = request.MountPoint,
                MountReadOnly = request.MountReadOnly
            };

            // 保存配置
            drive.Save();
        }

        /// <summary>
        /// 编辑云盘
        /// </summary>
        public void DriveEdit(string driveId, AliyunDriveEditRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.RefreshToken))
            {
                throw new LogicParamException();
            }

            var drive = AliyunStorageDb.Instance.DB.Get(driveId);
            if (drive == null)
            {
                throw new LogicException("云盘不存在");
            }

            if (_mounter.ContainsKey(drive.Id))
            {
                throw new LogicException("云盘已挂载，不可修改配置，如需修改，请先卸载挂载");
            }
            drive.RefreshToken = request.RefreshToken;

            drive.MountConfig ??= new AliyunDriveMountConfig();
            drive.MountConfig.IsRecycleBin = request.IsRecycleBin;
            drive.MountConfig.MountDrive = request.MountDrive;
            drive.MountConfig.MountOnStartup = request.MountOnStartup;
            drive.MountConfig.MountPath = request.MountPath;
            drive.MountConfig.MountPoint = request.MountPoint;
            drive.MountConfig.MountReadOnly = request.MountReadOnly;

            // 保存配置
            drive.Save();
        }

        /// <summary>
        /// 删除云盘
        /// </summary>
        public void DriveDelete(string driveId)
        {
            var drive = AliyunStorageDb.Instance.DB.Get(driveId);
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
        /// 挂载磁盘 - 云盘作业
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="mountPoint"></param>
        public void DriveJobMount(string jobId)
        {
            if (!Platform.IsClientWindows)
            {
                throw new LogicException("暂不支持非 Windows 系统挂载云盘，请等待下个版本发布！");
            }

            if (_jobs.TryGetValue(jobId, out var job) || job != null)
            {
                if (string.IsNullOrEmpty(job.CurrrentJob?.MountConfig?.MountPoint))
                {
                    throw new LogicException("请选择或输入挂载点");
                }

                var pints = Filesystem.GetAvailableMountPoints().ToList();
                if (pints.Count > 0 && !pints.Contains(job.CurrrentJob?.MountConfig?.MountPoint))
                {
                    throw new LogicException("选择的挂载点不存在或被占用，不允许挂载");
                }

                job.DriveMount();
            }
        }

        /// <summary>
        /// 卸载磁盘挂载 - 云盘作业
        /// </summary>
        /// <param name="jobId"></param>
        public void DriveJobUnmount(string jobId)
        {
            if (_jobs.TryGetValue(jobId, out var job) || job != null)
            {
                job.DriveUnmount();
            }
        }

        /// <summary>
        /// 挂载磁盘 - 云盘
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="mountPoint"></param>
        public void DriveMount(string driveId)
        {
            if (!Platform.IsClientWindows)
            {
                throw new LogicException("暂不支持非 Windows 系统挂载云盘，请等待下个版本发布！");
            }

            var ds = AliyunStorageDb.Instance.DB.GetAll();
            var drive = ds.FirstOrDefault(x => x.Id == driveId);
            if (drive == null)
                throw new LogicException("云盘不存在");

            if (_mounter.ContainsKey(driveId))
            {
                throw new LogicException("云盘已挂载，请不要重复挂载");
            }

            if (string.IsNullOrEmpty(drive.MountConfig.MountPoint))
            {
                throw new LogicException("请选择或输入挂载点");
            }

            var pints = Filesystem.GetAvailableMountPoints().ToList();
            if (pints.Count > 0 && !pints.Contains(drive.MountConfig.MountPoint))
            {
                throw new LogicException("选择的挂载点不存在或被占用，不允许挂载");
            }

            if (!_mounter.TryGetValue(drive.Id, out var mt) || mt == null)
            {
                mt = new AliyunDriveMounter(drive, drive.MountConfig);

                //mt.AliyunDriveInitFiles();

                mt.Mount();
                _mounter[drive.Id] = mt;
            }
        }

        /// <summary>
        /// 卸载磁盘挂载 - 云盘
        /// </summary>
        /// <param name="driveId"></param>
        public void DriveUnmount(string driveId)
        {
            if (_mounter.TryGetValue(driveId, out var mt) && mt != null)
            {
                mt?.Unmount();
                mt?.Dispose();
                _mounter.TryRemove(driveId, out _);
            }
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