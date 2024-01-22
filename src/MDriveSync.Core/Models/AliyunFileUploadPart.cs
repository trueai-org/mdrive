namespace MDriveSync.Core.Models
{
    /// <summary>
    /// 文件分块上传处理
    /// </summary>
    public class AliyunFileUploadPart
    {
        /// <summary>
        /// 分块序号，从 1 开始
        /// </summary>
        public int PartNumber { get; private set; }

        /// <summary>
        /// 本地上传文件路径
        /// </summary>
        public string LocalFilePath { get; private set; }

        /// <summary>
        /// 云盘上传路径
        /// </summary>
        public string UploadUrl { get; private set; }

        /// <summary>
        /// 当前已写入的文件大小
        /// </summary>
        public int CurrentSize { get; set; }

        /// <summary>
        /// 文件的总大小
        /// </summary>
        public long TotalSize { get; set; }

        /// <summary>
        /// 是否已上传到云盘
        /// </summary>
        public bool IsUploaded { get; set; }

        /// <summary>
        /// 文件ID。
        /// </summary>
        public string FileId { get; set; }

        /// <summary>
        /// 上传ID。
        /// </summary>
        public string UploadId { get; set; }

        public AliyunFileUploadPart(int partNumber, string localFilePath, string uploadUrl)
        {
            PartNumber = partNumber;
            LocalFilePath = localFilePath;
            UploadUrl = uploadUrl;
            CurrentSize = 0;
            IsUploaded = false;
        }
    }
}
