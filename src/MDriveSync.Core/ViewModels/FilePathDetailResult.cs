namespace MDriveSync.Core.ViewModels
{
    /// <summary>
    /// 云盘文件信息和本地文件信息
    /// </summary>
    public class FilePathDetailResult : AliyunDriveFileItem
    {
        /// <summary>
        /// 相对路径
        /// 不包含 / 前缀
        /// </summary>
        public string Key { get; set; }
    }
}