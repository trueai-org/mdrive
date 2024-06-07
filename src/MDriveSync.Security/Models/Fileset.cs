using LiteDB;
using ServiceStack.DataAnnotations;

namespace MDriveSync.Security.Models
{
    /// <summary>
    /// 文件信息
    /// </summary>
    public class Fileset : IBaseId
    {
        public Fileset()
        {
        }

        /// <summary>
        /// 文件 ID
        /// </summary>
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// 文件 key
        /// 文件的 path = RootPackage.Key
        /// </summary>
        [Index]
        [BsonField("k")]
        public string Key { get; set; }

        /// <summary>
        /// 源文件 key
        /// </summary>
        [BsonField("sk")]
        public string SourceKey { get; set; }

        /// <summary>
        /// 源文件大小
        /// </summary>
        [BsonField("z")]
        public long Size { get; set; }

        /// <summary>
        /// 源文件 hash
        /// </summary>
        [BsonField("h")]
        public string Hash { get; set; }

        /// <summary>
        /// 源文件创建时间
        /// </summary>
        [BsonField("c")]
        public long Created { get; set; }

        /// <summary>
        /// 源文件修改时间
        /// </summary>
        [BsonField("u")]
        public long Updated { get; set; }

        /// <summary>
        /// 是否为影子文件（说明是重复文件）
        /// 表示文件只是一个指向原始数据的指针、表示文件是对原始文件的引用。
        /// </summary>
        [BsonField("s")]
        public bool IsShadow { get; set; }
    }
}