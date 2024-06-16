using MDriveSync.Core.Models;

namespace MDriveSync.Core.DB
{
    /// <summary>
    /// 本地存储数据库
    /// </summary>
    public class LocalStorageDb : SingletonBase<LocalStorageDb>
    {
        private readonly LiteRepository<LocalStorageConfig, string> _db = new("drive.db", false);

        public LiteRepository<LocalStorageConfig, string> DB
        {
            get
            {
                return _db;
            }
        }
    }
}