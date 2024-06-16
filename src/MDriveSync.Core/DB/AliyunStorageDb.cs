using MDriveSync.Core.Options;

namespace MDriveSync.Core.DB
{
    /// <summary>
    /// 阿里云盘数据库
    /// </summary>
    public class AliyunStorageDb : SingletonBase<AliyunStorageDb>
    {
        private readonly LiteRepository<AliyunStorageConfig, string> _db = new("drive.db", false);

        public LiteRepository<AliyunStorageConfig, string> DB
        {
            get
            {
                return _db;
            }
        }
    }
}