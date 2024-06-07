using LiteDB;
using ServiceStack.DataAnnotations;

namespace MDriveSync.Security.Models
{
    /// <summary>
    /// 根目录文件信息（记录处理过的源文件和映射文件）
    /// </summary>
    public class RootFileset : IBaseId
    {
        public RootFileset()
        {
        }

        /// <summary>
        /// ID
        /// </summary>
        //[PrimaryKey]
        //[AutoIncrement]
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// 包 ID
        /// </summary>
        [Index]
        [BsonField("p")]
        public int RootPackageId { get; set; }

        /// <summary>
        /// 源文件 key
        /// E:/gits/xxx/xxx/xx.xx
        /// </summary>
        [Index]
        [BsonField("sk")]
        public string FilesetSourceKey { get; set; }

        /// <summary>
        /// 源文件 hash
        /// 0 字节文件没有 hash
        /// </summary>
        [BsonField("h")]
        public string FilesetHash { get; set; }

        /// <summary>
        /// 源文件大小
        /// </summary>
        [BsonField("z")]
        public long FilesetSize { get; set; }

        /// <summary>
        /// 源文件创建时间
        /// </summary>
        [BsonField("c")]
        public long FilesetCreated { get; set; }

        /// <summary>
        /// 源文件修改时间
        /// </summary>
        [BsonField("u")]
        public long FilesetUpdated { get; set; }

        /// <summary>
        /// 是否为影子文件（说明是重复文件）
        /// 表示文件只是一个指向原始数据的指针、表示文件是对原始文件的引用。
        /// </summary>
        [BsonField("s")]
        public bool IsShadow { get; set; }
    }
}