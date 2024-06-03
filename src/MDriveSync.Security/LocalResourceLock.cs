using System.Collections.Concurrent;

namespace MDriveSync.Security
{
    /// <summary>
    /// 本地资源锁，支持基于键的锁管理
    /// </summary>
    public class LocalResourceLock
    {
        private static readonly ConcurrentDictionary<string, object> _locks = new();

        /// <summary>
        /// 获取指定键的锁，并进入该锁。
        /// </summary>
        /// <param name="key">用于获取锁的键。</param>
        public static void EnterLock(string key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key), "键不能为空");

            var lockObject = _locks.GetOrAdd(key, _ => new object());
            Monitor.Enter(lockObject);
        }

        /// <summary>
        /// 退出指定键的锁。
        /// </summary>
        /// <param name="key">用于获取锁的键。</param>
        public static void ExitLock(string key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key), "键不能为空");

            if (_locks.TryGetValue(key, out var lockObject))
            {
                Monitor.Exit(lockObject);
            }
        }

        /// <summary>
        /// 执行带有锁的操作。
        /// </summary>
        /// <param name="key">用于获取锁的键。</param>
        /// <param name="action">要执行的操作。</param>
        public static void Lock(string key, Action action)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key), "键不能为空");

            if (action == null)
                throw new ArgumentNullException(nameof(action), "操作不能为空");

            EnterLock(key);

            try
            {
                action();
            }
            finally
            {
                ExitLock(key);
            }
        }
    }
}