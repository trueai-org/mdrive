using System.Collections.Concurrent;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 异步简单锁
    /// </summary>
    public class AsyncLock
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly Task<IDisposable> _releaser;

        public AsyncLock()
        {
            _releaser = Task.FromResult((IDisposable)new Releaser(this));
        }

        public async Task<IDisposable> LockAsync()
        {
            await _semaphore.WaitAsync();
            return _releaser.Result;
        }

        private class Releaser : IDisposable
        {
            private readonly AsyncLock _toRelease;

            internal Releaser(AsyncLock toRelease)
            {
                _toRelease = toRelease;
            }

            public void Dispose()
            {
                _toRelease._semaphore.Release();
            }
        }
    }

    /// <summary>
    /// 异步资源锁
    /// </summary>
    public class AsyncLockV2
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphoreDictionary = new();

        public async Task<IDisposable> LockAsync(string resource = "")
        {
            var semaphore = _semaphoreDictionary.GetOrAdd(resource, k => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            return new Releaser(() => Release(resource));
        }

        private void Release(string resource)
        {
            if (_semaphoreDictionary.TryGetValue(resource, out var semaphore))
            {
                semaphore.Release();
            }
        }

        private class Releaser : IDisposable
        {
            private readonly Action _releaseAction;

            internal Releaser(Action releaseAction)
            {
                _releaseAction = releaseAction;
            }

            public void Dispose()
            {
                _releaseAction();
            }
        }
    }
}