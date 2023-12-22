using System.Text.Json.Serialization;

namespace MDriveSync.Core
{
    /// <summary>
    /// 文件上传 - 文件创建
    /// https://www.yuque.com/aliyundrive/zpfszx/ezlzok
    /// </summary>
    public class AliyunDriveOpenFileCreateResponse
    {
        /// <summary>
        /// 文件是否被删除。
        /// </summary>
        [JsonPropertyName("trashed")]
        public bool? Trashed { get; set; }

        /// <summary>
        /// 文件名。
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// 文件缩略图。
        /// </summary>
        [JsonPropertyName("thumbnail")]
        public string Thumbnail { get; set; }

        /// <summary>
        /// 文件类型（如文件或文件夹）。
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// 文件分类。
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; }

        /// <summary>
        /// 文件是否隐藏。
        /// </summary>
        [JsonPropertyName("hidden")]
        public bool? Hidden { get; set; }

        /// <summary>
        /// 文件状态。
        /// </summary>
        [JsonPropertyName("status")]
        public string Status { get; set; }

        /// <summary>
        /// 文件描述。
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// 文件元数据。
        /// </summary>
        [JsonPropertyName("meta")]
        public string Meta { get; set; }

        /// <summary>
        /// 文件URL。
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; }

        /// <summary>
        /// 文件大小。
        /// </summary>
        [JsonPropertyName("size")]
        public long? Size { get; set; }

        /// <summary>
        /// 文件是否加星标。
        /// </summary>
        [JsonPropertyName("starred")]
        public bool? Starred { get; set; }

        /// <summary>
        /// 文件是否可用。
        /// </summary>
        [JsonPropertyName("available")]
        public bool? Available { get; set; }

        /// <summary>
        /// 文件是否存在。
        /// </summary>
        [JsonPropertyName("exist")]
        public bool? Exist { get; set; }

        /// <summary>
        /// 用户标签。
        /// </summary>
        [JsonPropertyName("user_tags")]
        public string UserTags { get; set; }

        /// <summary>
        /// 文件MIME类型。
        /// </summary>
        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; }

        /// <summary>
        /// 父文件ID。
        /// </summary>
        [JsonPropertyName("parent_file_id")]
        public string ParentFileId { get; set; }

        /// <summary>
        /// 驱动ID。
        /// </summary>
        [JsonPropertyName("drive_id")]
        public string DriveId { get; set; }

        /// <summary>
        /// 文件ID。
        /// </summary>
        [JsonPropertyName("file_id")]
        public string FileId { get; set; }

        /// <summary>
        /// 文件扩展名。
        /// </summary>
        [JsonPropertyName("file_extension")]
        public string FileExtension { get; set; }

        /// <summary>
        /// 修订ID。
        /// </summary>
        [JsonPropertyName("revision_id")]
        public string RevisionId { get; set; }

        /// <summary>
        /// 内容哈希值。
        /// </summary>
        [JsonPropertyName("content_hash")]
        public string ContentHash { get; set; }

        /// <summary>
        /// 内容哈希名称。
        /// </summary>
        [JsonPropertyName("content_hash_name")]
        public string ContentHashName { get; set; }

        /// <summary>
        /// 加密模式。
        /// </summary>
        [JsonPropertyName("encrypt_mode")]
        public string EncryptMode { get; set; }

        /// <summary>
        /// 域ID。
        /// </summary>
        [JsonPropertyName("domain_id")]
        public string DomainId { get; set; }

        /// <summary>
        /// 下载URL。
        /// </summary>
        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; }

        /// <summary>
        /// 用户自定义元数据。
        /// </summary>
        [JsonPropertyName("user_meta")]
        public string UserMeta { get; set; }

        /// <summary>
        /// 内容类型。
        /// </summary>
        [JsonPropertyName("content_type")]
        public string ContentType { get; set; }

        /// <summary>
        /// 创建时间。
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 更新时间。
        /// </summary>
        [JsonPropertyName("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 本地创建时间。
        /// </summary>
        [JsonPropertyName("local_created_at")]
        public DateTime? LocalCreatedAt { get; set; }

        /// <summary>
        /// 本地修改时间。
        /// </summary>
        [JsonPropertyName("local_modified_at")]
        public DateTime? LocalModifiedAt { get; set; }

        /// <summary>
        /// 删除时间。
        /// </summary>
        [JsonPropertyName("trashed_at")]
        public DateTime? TrashedAt { get; set; }

        /// <summary>
        /// 惩罚标志。
        /// </summary>
        [JsonPropertyName("punish_flag")]
        public bool? PunishFlag { get; set; }

        /// <summary>
        /// 文件名。
        /// </summary>
        [JsonPropertyName("file_name")]
        public string FileName { get; set; }

        /// <summary>
        /// 上传ID。
        /// </summary>
        [JsonPropertyName("upload_id")]
        public string UploadId { get; set; }

        /// <summary>
        /// 位置。
        /// </summary>
        [JsonPropertyName("location")]
        public string Location { get; set; }

        /// <summary>
        /// 是否快速上传。
        /// </summary>
        [JsonPropertyName("rapid_upload")]
        public bool RapidUpload { get; set; }

        /// <summary>
        /// 分片信息列表。
        /// </summary>
        [JsonPropertyName("part_info_list")]
        public List<AliyunDriveOpenFileCreatePartInfo> PartInfoList { get; set; }

        /// <summary>
        /// 流上传信息。
        /// </summary>
        [JsonPropertyName("streams_upload_info")]
        public string StreamsUploadInfo { get; set; }

        /// <summary>
        /// 分片信息类。
        /// </summary>
        public class AliyunDriveOpenFileCreatePartInfo
        {
            /// <summary>
            /// ETag。
            /// </summary>
            [JsonPropertyName("etag")]
            public string Etag { get; set; }

            /// <summary>
            /// 分片编号。
            /// </summary>
            [JsonPropertyName("part_number")]
            public int? PartNumber { get; set; }

            /// <summary>
            /// 分片大小。
            /// </summary>
            [JsonPropertyName("part_size")]
            public long? PartSize { get; set; }

            /// <summary>
            /// 上传URL。
            /// </summary>
            [JsonPropertyName("upload_url")]
            public string UploadUrl { get; set; }

            /// <summary>
            /// 内容类型。
            /// </summary>
            [JsonPropertyName("content_type")]
            public string ContentType { get; set; }

            /// <summary>
            /// 上传表单信息。
            /// </summary>
            [JsonPropertyName("upload_form_info")]
            public string UploadFormInfo { get; set; }
        }
    }
}