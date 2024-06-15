namespace MDriveSync.Core.DB
{
    /// <summary>
    /// 阿里云盘数据库
    /// </summary>
    public class AliyunDriveDb : SingletonBase<AliyunDriveDb>
    {
        private readonly LiteRepository<AliyunDriveConfig, string> _db = new("drive.db", false);

        public LiteRepository<AliyunDriveConfig, string> DB
        {
            get
            {
                return _db;
            }
        }
    }
}