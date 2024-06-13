using System.Text.Json.Serialization;

namespace MDriveSync.Core
{
    /// <summary>
    /// 云盘文件信息
    /// </summary>
    public class AliyunDriveFileItem
    {
        /// <summary>
        /// 当前文件绝对路径 key
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// 父级文件绝对路径 key
        /// </summary>
        public string ParentKey { get; set; }

        /// <summary>
        /// 驱动器ID。
        /// </summary>
        [JsonPropertyName("drive_id")]
        public string DriveId { get; set; }

        /// <summary>
        /// 文件ID。
        /// </summary>
        [JsonPropertyName("file_id")]
        public string FileId { get; set; }

        /// <summary>
        /// 父文件夹ID。
        /// </summary>
        [JsonPropertyName("parent_file_id")]
        public string ParentFileId { get; set; }

        /// <summary>
        /// 文件名。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// 文件夹名称（仅文件夹时有效）
        /// </summary>
        [JsonPropertyName("file_name")]
        public string FileName { get; set; }

        /// <summary>
        /// 文件大小。
        /// </summary>
        [JsonPropertyName("size")]
        public long? Size { get; set; }

        /// <summary>
        /// 文件扩展名。
        /// </summary>
        [JsonPropertyName("file_extension")]
        public string FileExtension { get; set; }

        /// <summary>
        /// 文件内容哈希。
        /// </summary>
        [JsonPropertyName("content_hash")]
        public string ContentHash { get; set; }

        /// <summary>
        /// 文件内容哈希的算法名称，例如“SHA1”。
        /// </summary>
        [JsonPropertyName("content_hash_name")]
        public string ContentHashName { get; set; }

        /// <summary>
        /// 文件分类。
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; }

        /// <summary>
        /// 类型（文件或文件夹）
        /// file | folder
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// 是否为文件
        /// </summary>
        public bool IsFile => Type == "file";

        /// <summary>
        /// 是否为文件夹
        /// </summary>
        public bool IsFolder => Type == "folder";

        /// <summary>
        /// 预览链接。
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; }

        /// <summary>
        /// 创建时间。
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>
        /// 更新时间。
        /// </summary>
        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }

        /// <summary>
        /// 文件的MIME类型，如'image/jpeg'、'application/pdf'等。
        /// </summary>
        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; }

        /// <summary>
        /// 文件状态，例如“可用”、“已删除”等。
        /// available
        /// </summary>
        [JsonPropertyName("status")]
        public string Status { get; set; }

        /// <summary>
        /// 本地文件名（如果匹配到本地文件）
        /// </summary>
        public string LocalFileName { get; set; }
    }

    /// <summary>
    /// 云盘列表信息
    /// </summary>
    public class AliyunFileList
    {
        /// <summary>
        /// 文件项列表。
        /// </summary>
        [JsonPropertyName("items")]
        public List<AliyunDriveFileItem> Items { get; set; }

        /// <summary>
        /// 下一个分页标记。
        /// </summary>
        [JsonPropertyName("next_marker")]
        public string NextMarker { get; set; }
    }
}