namespace MDriveSync.Core.DB
{
    /// <summary>
    /// 云盘数据库
    /// </summary>
    public class DriveDb
    {
        private static readonly object _lock = new();
        private static LiteRepository<AliyunDriveConfig, string> _instacne;

        public static LiteRepository<AliyunDriveConfig, string> Instacne
        {
            get
            {
                if (_instacne != null)
                {
                    return _instacne;
                }

                lock (_lock)
                {
                    _instacne ??= new("drive.db", true);
                }

                return _instacne;
            }
        }
    }
}