using ServiceStack.DataAnnotations;

namespace MDriveSync.Security.Models
{
    /// <summary>
    /// 根目录文件信息（记录处理过的源文件和映射文件）
    /// </summary>
    public class RootFileset : IBaseId
    {
        /// <summary>
        /// ID
        /// </summary>
        [PrimaryKey]
        [AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// 包 ID
        /// </summary>
        [Index]
        public int RootPackageId { get; set; }

        /// <summary>
        /// 源文件 key
        /// E:/gits/xxx/xxx/xx.xx
        /// </summary>
        [Index]
        public string FilesetSourceKey { get; set; }

        /// <summary>
        /// 源文件 hash
        /// </summary>
        public string FilesetHash { get; set; }
    }
}
