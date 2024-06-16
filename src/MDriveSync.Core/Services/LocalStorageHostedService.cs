using MDriveSync.Core.DB;
using MDriveSync.Core.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace MDriveSync.Core
{
    /// <summary>
    /// 本地存储作业服务
    /// 定时检查服务
    /// 定时服务就可以在后台运行，且不会影响应用程序的启动和发布过程
    /// </summary>
    public class LocalStorageHostedService : BackgroundService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly ClientOptions _clientOptions;
        private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
        private readonly LiteRepository<LocalStorageConfig, string> _db = LocalStorageDb.Instance.DB;

        // 作业
        private readonly ConcurrentDictionary<string, LocalStorageJob> _jobs = new();

        private Timer _timer;

        public LocalStorageHostedService(ILogger<LocalStorageHostedService> logger, IOptionsMonitor<ClientOptions> clientOptions)
        {
            _logger = logger;
            _clientOptions = clientOptions?.CurrentValue;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            stoppingToken.Register(() => _logger.LogDebug($"例行检查服务已停止"));

            // 启动时，如果有配置，则新增到数据库
            // 如果数据库已有，则跳过
            if (_clientOptions?.LocalStorages?.Count > 0)
            {
                InitAddJob(_clientOptions.LocalStorages);
            }

            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

            return Task.CompletedTask;
        }

        /// <summary>
        /// 初始化新增作业
        /// </summary>
        public void InitAddJob(List<LocalStorageConfig> localStorageConfigs)
        {
            var drives = _db.GetAll();
            foreach (var cd in localStorageConfigs)
            {
                var f = drives.FirstOrDefault(x => x.Id == cd.Id);
                if (f == null)
                {
                    _db.Add(cd);
                }
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

                var ds = _db.GetAll();

                foreach (var ad in ds)
                {
                    // 作业
                    var jobs = ad.Jobs.ToList();
                    foreach (var cf in jobs)
                    {
                        if (!_jobs.TryGetValue(cf.Id, out var job) || job == null)
                        {
                            job = new LocalStorageJob(ad, cf, _logger);
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
        public ConcurrentDictionary<string, LocalStorageJob> Jobs()
        {
            return _jobs;
        }

        /// <summary>
        /// 添加作业
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="cfg"></param>
        /// <exception cref="LogicException"></exception>
        public void JobAdd(string driveId, LocalJobConfig cfg)
        {
            var drives = _db.GetAll();
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

            drive.Jobs ??= new List<LocalJobConfig>();

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
                job = new LocalStorageJob(drive, cfg, _logger);
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
        /// 列表
        /// </summary>
        /// <returns></returns>
        public List<LocalStorageConfig> Drives()
        {
            var jobs = Jobs();

            var ds = _db.GetAll();
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
        /// 添加工作组
        /// </summary>
        public void DriveAdd(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new LogicParamException();
            }

            var drive = new LocalStorageConfig()
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Jobs = [],
            };

            // 保存配置
            drive.Save();
        }

        /// <summary>
        /// 编辑工作组
        /// </summary>
        public void DriveEdit(string driveId, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new LogicParamException();
            }

            var drive = _db.Get(driveId);
            if (drive == null)
            {
                throw new LogicException("工作组不存在");
            }

            drive.Name = name;

            // 保存配置
            drive.Save();
        }

        /// <summary>
        /// 删除云盘
        /// </summary>
        public void DriveDelete(string groupId)
        {
            var drive = _db.Get(groupId);
            if (drive == null)
            {
                throw new LogicException("工作组不存在");
            }

            // 清除作业
            foreach (var j in drive.Jobs)
            {
                JobDelete(j.Id);
            }

            // 保存配置
            drive.Save(true);
        }
    }
}