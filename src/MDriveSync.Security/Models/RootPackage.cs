using LiteDB;
using ServiceStack.DataAnnotations;

namespace MDriveSync.Security.Models
{
    /// <summary>
    /// 根目录包信息
    /// </summary>
    public class RootPackage : IBaseId
    {
        public RootPackage()
        {
        }

        /// <summary>
        /// 文件 ID
        /// </summary>
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// 相对路径 {category}{index % 256:x2}/{index}
        /// 示例：a00/0、a00/1 ... a00/88888
        /// 示例：a00~aff、b00~bff ... j00~jff
        /// </summary>
        [Index]
        [BsonField("k")]
        public string Key { get; set; }

        /// <summary>
        /// 包分类（a~j）
        /// </summary>
        [BsonField("c")]
        public string Category { get; set; }

        /// <summary>
        /// 包索引值，从 0 开始
        /// 每个包都是1个或多个完整的文件
        /// </summary>
        [BsonField("i")]
        public int Index { get; set; }

        /// <summary>
        /// 包大小（影子文件不占用包大小）
        /// </summary>
        [BsonField("z")]
        public long Size { get; set; }

        /// <summary>
        /// 是否为多文件包（包中是否包含多个文件）
        /// </summary>
        [BsonField("m")]
        public bool Multifile { get; set; }
    }
}