using System.Text.Json.Serialization;

namespace MDriveSync.Core
{
    /// <summary>
    /// 移动文件或文件夹
    /// </summary>
    public class AliyunDriveOpenFileMoveResponse
    {
        /// <summary>
        /// 云盘 ID
        /// </summary>
        [JsonPropertyName("drive_id")]
        public string DriveId { get; set; }

        /// <summary>
        /// 文件 ID
        /// </summary>
        [JsonPropertyName("file_id")]
        public string FileId { get; set; }

        /// <summary>
        /// 异步任务id，有的话表示需要经过异步处理。
        /// </summary>
        [JsonPropertyName("async_task_id")]
        public string AsyncTaskId { get; set; }

        /// <summary>
        /// 文件是否已存在
        /// </summary>
        [JsonPropertyName("exist")]
        public bool Exist { get; set; }
    }
}