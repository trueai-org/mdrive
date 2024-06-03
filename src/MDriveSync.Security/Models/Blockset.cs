using ServiceStack.DataAnnotations;

namespace MDriveSync.Security.Models
{
    /// <summary>
    /// 块信息
    /// </summary>
    public class Blockset : IBaseId
    {
        /// <summary>
        /// 块 ID
        /// </summary>
        [PrimaryKey]
        [AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// 文件 ID，对应 Fileset.Id
        /// </summary>
        [Index]
        public int FilesetId { get; set; }

        /// <summary>
        /// 块索引，从 0 开始
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 块大小
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// 块 hash
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// 数据的起始位置
        /// </summary>
        public long StartIndex { get; set; }

        /// <summary>
        /// 数据的结束位置
        /// </summary>
        public long EndIndex { get; set; }
    }
}