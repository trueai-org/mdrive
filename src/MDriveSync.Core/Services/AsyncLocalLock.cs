using System.Collections.Concurrent;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 本地资源锁
    /// </summary>
    public static class AsyncLocalLock
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _lockObjs = new();

        /// <summary>
        /// 异步获取锁
        /// </summary>
        /// <param name="key">资源标识符</param>
        /// <param name="span">等待时间</param>
        /// <returns>是否成功获取锁</returns>
        private static async Task<bool> LockEnterAsync(string key, TimeSpan span)
        {
            var semaphore = _lockObjs.GetOrAdd(key, k => new SemaphoreSlim(1, 1));
            return await semaphore.WaitAsync(span);
        }

        /// <summary>
        /// 释放锁
        /// </summary>
        /// <param name="key">资源标识符</param>
        private static void LockExit(string key)
        {
            if (_lockObjs.TryGetValue(key, out SemaphoreSlim semaphore) && semaphore != null)
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// 异步等待并执行操作
        /// </summary>
        /// <param name="resource">资源标识符</param>
        /// <param name="expirationTime">超时时间</param>
        /// <param name="action">要执行的操作</param>
        /// <returns>是否成功执行操作</returns>
        public static async Task<bool> TryLockAsync(string resource, TimeSpan expirationTime, Func<Task> action)
        {
            if (await LockEnterAsync(resource, expirationTime))
            {
                try
                {
                    await action();
                    return true;
                }
                finally
                {
                    LockExit(resource);
                }
            }
            return false;
        }
    }
}