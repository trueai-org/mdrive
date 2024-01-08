namespace MDriveSync.Core.DB
{
    /// <summary>
    /// 云盘数据库
    /// </summary>
    public class DriveDb
    {
        /// <summary>
        /// 
        /// </summary>
        public static readonly LiteRepository<AliyunDriveConfig, string> Instacne = new("drive.db", true);
    }
}
