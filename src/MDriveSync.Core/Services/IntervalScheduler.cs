using Serilog;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 基于时间间隔的任务调度器
    /// </summary>
    public class IntervalScheduler : IDisposable
    {
        private readonly Timer _timer;                // 定时器
        private readonly TimeSpan _interval;          // 执行间隔
        private readonly Action _taskToRun;           // 要执行的任务
        private readonly bool _executeImmediately;    // 是否立即执行
        private readonly object _lockObject = new();  // 锁对象，用于控制并发
        private readonly CancellationTokenSource _cts; // 取消令牌源
        private bool _isRunning;                      // 标记任务是否正在运行
        private bool _disposed;                       // 标记是否已释放资源
        private bool _started;                        // 标记是否已启动

        // 上次执行时间
        private DateTime _lastRunTime = DateTime.MinValue;

        /// <summary>
        /// 初始化基于时间间隔的任务调度器
        /// </summary>
        /// <param name="intervalInSeconds">执行间隔（秒）</param>
        /// <param name="taskToRun">要执行的任务</param>
        /// <param name="executeImmediately">是否立即执行，默认为false</param>
        public IntervalScheduler(int intervalInSeconds, Action taskToRun, bool executeImmediately = false)
        {
            if (intervalInSeconds <= 0)
                throw new ArgumentException("执行间隔必须大于0", nameof(intervalInSeconds));

            _interval = TimeSpan.FromSeconds(intervalInSeconds);
            _taskToRun = taskToRun ?? throw new ArgumentNullException(nameof(taskToRun));
            _executeImmediately = executeImmediately;
            _timer = new Timer(TimerCallback);
            _cts = new CancellationTokenSource();

            Log.Debug("初始化定时任务调度器，间隔: {IntervalSeconds}秒，立即执行: {ExecuteImmediately}",
                intervalInSeconds, executeImmediately);
        }

        /// <summary>
        /// 开始调度任务
        /// </summary>
        public void Start()
        {
            lock (_lockObject)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(IntervalScheduler));

                if (_started)
                    return;

                _started = true;

                // 记录开始时间
                _lastRunTime = DateTime.Now;
                Log.Information("开始定时任务调度，当前时间: {StartTime}", _lastRunTime);

                // 设置定时器
                if (_executeImmediately)
                {
                    // 立即执行，然后按间隔执行
                    _timer.Change(TimeSpan.Zero, _interval);
                    Log.Debug("定时任务将立即执行一次，然后每隔 {Interval} 执行", _interval);
                }
                else
                {
                    // 等待一个间隔后再执行
                    _timer.Change(_interval, _interval);
                    Log.Debug("定时任务将在 {Interval} 后首次执行", _interval);
                }
            }
        }

        /// <summary>
        /// 获取下次执行时间
        /// </summary>
        public DateTime GetNextRunTime()
        {
            lock (_lockObject)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(IntervalScheduler));

                // 如果还没有执行过，并且设置了立即执行，返回当前时间
                if (_lastRunTime == DateTime.MinValue && _executeImmediately && !_started)
                    return DateTime.Now;

                // 如果还没有执行过，并且没有设置立即执行，返回启动后的第一个间隔
                if (_lastRunTime == DateTime.MinValue && !_executeImmediately && !_started)
                    return DateTime.Now.Add(_interval);

                // 计算下次执行时间，基于上次执行时间
                DateTime nextRunTime = _lastRunTime.Add(_interval);

                // 如果下次执行时间已经过了，则计算下一个有效的执行时间点
                if (nextRunTime < DateTime.Now)
                {
                    // 计算已经过去的间隔数
                    TimeSpan elapsed = DateTime.Now - _lastRunTime;
                    int intervals = (int)(elapsed.TotalSeconds / _interval.TotalSeconds) + 1;

                    // 计算新的下次执行时间
                    nextRunTime = _lastRunTime.AddSeconds(intervals * _interval.TotalSeconds);
                }

                return nextRunTime;
            }
        }

        /// <summary>
        /// 停止调度任务
        /// </summary>
        public void Stop()
        {
            lock (_lockObject)
            {
                if (_disposed || !_started)
                    return;

                _started = false;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                Log.Information("停止定时任务调度");
            }
        }

        /// <summary>
        /// 取消当前正在执行的任务和所有未来任务
        /// </summary>
        public void Cancel()
        {
            lock (_lockObject)
            {
                if (_disposed)
                    return;

                Stop();
                try
                {
                    _cts.Cancel();
                    Log.Information("已取消定时任务");
                }
                catch (ObjectDisposedException)
                {
                    // 忽略已释放的对象异常
                }
            }
        }

        /// <summary>
        /// 手动触发任务执行一次（不影响原有调度）
        /// </summary>
        public void TriggerNow()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IntervalScheduler));

            Log.Information("手动触发定时任务执行");
            Task.Run(ExecuteTask);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        /// <param name="disposing">是否正在释放托管资源</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Stop();
                    _cts.Dispose();
                    _timer.Dispose();
                    Log.Debug("定时任务调度器已释放资源");
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// 定时器回调方法
        /// </summary>
        private void TimerCallback(object state)
        {
            if (_cts.IsCancellationRequested)
            {
                Stop();
                return;
            }

            ExecuteTask();
        }

        /// <summary>
        /// 执行任务
        /// </summary>
        private void ExecuteTask()
        {
            // 检查是否已取消
            if (_cts.IsCancellationRequested)
                return;

            // 使用锁确保同一时间只有一个任务在执行
            lock (_lockObject)
            {
                if (_isRunning || _disposed)
                    return;

                _isRunning = true;
            }

            try
            {
                Log.Debug("开始执行定时任务");
                _taskToRun();
                Log.Debug("定时任务执行完成");
            }
            catch (OperationCanceledException)
            {
                Log.Information("定时任务已取消");
            }
            catch (Exception ex)
            {
                // 任务执行异常不应影响调度器继续工作
                Log.Error(ex, "定时任务调度器执行异常");
            }
            finally
            {
                lock (_lockObject)
                {
                    _isRunning = false;
                    _lastRunTime = DateTime.Now;
                }
            }
        }

        /// <summary>
        /// 终结器
        /// </summary>
        ~IntervalScheduler()
        {
            Dispose(false);
        }
    }
}