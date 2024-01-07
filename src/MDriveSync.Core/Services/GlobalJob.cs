using System.Collections.Concurrent;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 全局作业管理，保证执行中的作业只有 1 个
    /// </summary>
    public class GlobalJob
    {
        private static readonly object _lockObject = new();
        private static readonly SemaphoreSlim _globalLock = new(1, 1);
        private static readonly ConcurrentQueue<string> _globalJobQueue = new();
        private static readonly ConcurrentDictionary<string, (Func<CancellationToken, Task>, CancellationTokenSource, bool)> _globalJobs = new();

        private static Task _loggingTask;
        private static GlobalJob _instance;
        private static ManualResetEvent _mre; // 信号

        private GlobalJob()
        { }

        public static GlobalJob Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                if (_loggingTask == null)
                    Interlocked.CompareExchange(ref _loggingTask, _loggingTask = new Task(Running, TaskCreationOptions.LongRunning), null);

                if (_mre == null)
                    Interlocked.CompareExchange(ref _mre, new ManualResetEvent(false), null);

                if (_loggingTask.Status == TaskStatus.Created)
                {
                    lock (_lockObject)
                    {
                        if (_loggingTask.Status == TaskStatus.Created)
                            _loggingTask.Start();
                    }
                }

                if (_instance == null)
                    Interlocked.CompareExchange(ref _instance, new GlobalJob(), null);

                return _instance;
            }
        }

        private static async void Running()
        {
            while (true)
            {
                // 等待信号通知
                _mre.WaitOne();

                while (_globalJobQueue.TryDequeue(out var jobId))
                {
                    // 如果作业中没有，则跳过
                    if (!_globalJobs.ContainsKey(jobId))
                    {
                        continue;
                    }

                    var (jobFunc, cancellationTokenSource, isCompleted) = _globalJobs[jobId];

                    // 如果是未完成并且是未取消的
                    if (!isCompleted && !cancellationTokenSource.IsCancellationRequested)
                    {
                        await _globalLock.WaitAsync();
                        try
                        {
                            await jobFunc(cancellationTokenSource.Token);
                            _globalJobs[jobId] = (jobFunc, cancellationTokenSource, true); // 标记作业为已完成
                        }
                        catch (OperationCanceledException)
                        {
                            Console.WriteLine($"任务 {jobId} 被取消。");
                        }
                        finally
                        {
                            _globalLock.Release();
                        }
                    }
                    else
                    {
                        // 如果是已完成或已取消，则不再处理
                        _globalJobs.TryRemove(jobId, out _);
                    }
                }

                // 重新设置信号
                _mre.Reset();
            }
        }

        /// <summary>
        /// 添加或重启作业
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="jobFunc"></param>
        public void AddOrRestartJob(string jobId, Func<CancellationToken, Task> jobFunc)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            var jobExists = _globalJobs.TryGetValue(jobId, out var existingJob);

            if (!jobExists || (jobExists && existingJob.Item3)) // 如果作业不存在或已完成
            {
                _globalJobs[jobId] = (jobFunc, cancellationTokenSource, false); // 设置作业未执行
                _globalJobQueue.Enqueue(jobId);
            }
            else if (jobExists && !existingJob.Item3) // 如果作业存在且未完成
            {
                existingJob.Item2.Cancel(); // 取消当前作业
                _globalJobs[jobId] = (jobFunc, new CancellationTokenSource(), false); // 用新的 CancellationTokenSource 重置作业
                _globalJobQueue.Enqueue(jobId);
            }

            // 通知信号器
            _mre.Set();
        }

        /// <summary>
        /// 取消作业
        /// </summary>
        /// <param name="jobId"></param>
        public void CancelJob(string jobId)
        {
            if (_globalJobs.TryGetValue(jobId, out var job))
            {
                job.Item2.Cancel();
            }
        }
    }
}