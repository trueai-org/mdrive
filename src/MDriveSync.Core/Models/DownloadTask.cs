﻿using MDriveSync.Core.DB;
using ServiceStack.DataAnnotations;
using System.Reflection.Metadata.Ecma335;

namespace MDriveSync.Core.Models
{
    /// <summary>
    /// 下载管理器设置
    /// </summary>
    public class DownloadManagerSetting : IBaseId<string>
    {
        public string Id { get; set; } = "default";

        /// <summary>
        /// 默认下载路径
        /// </summary>
        public string DefaultDownload { get; set; }

        /// <summary>
        /// 最大并行下载数
        /// </summary>
        public int MaxParallelDownload { get; set; }

        /// <summary>
        /// 下载速度限制（字节/秒）
        /// </summary>
        public int DownloadSpeedLimit { get; set; }
    }

    /// <summary>
    /// 表示一个下载任务。
    /// </summary>
    public class DownloadTask : IBaseId<string>
    {
        /// <summary>
        /// 获取或设置下载任务的唯一标识符。
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 获取或设置下载文件的 URL。
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// 获取或设置下载文件保存的路径。
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// 获取或设置下载文件的名称。
        /// </summary>
        public string FileName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(FilePath))
                {
                    return Path.GetFileName(FilePath);
                }

                return Name;
            }
        }

        /// <summary>
        /// 阿里云文件名称（加密名称或非加密名称）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 默认保存的下载目录
        /// </summary>
        public string DefaultSavePath { get; set; }

        /// <summary>
        /// 文件大小
        /// </summary>
        public string FileSize => ((double)TotalBytes).ToFileSize();

        /// <summary>
        /// 获取或设置文件的总字节数。
        /// </summary>
        public long TotalBytes { get; set; }

        /// <summary>
        /// 获取或设置已下载的字节数。
        /// </summary>
        public long DownloadedBytes { get; set; }

        /// <summary>
        /// 获取或设置下载任务的状态。
        /// </summary>
        public DownloadStatus Status { get; set; }

        /// <summary>
        /// 状态名称
        /// </summary>
        public string StatusString => Status switch
        {
            DownloadStatus.Pending => "等待中",
            DownloadStatus.Downloading => "下载中",
            DownloadStatus.Paused => "已暂停",
            DownloadStatus.Completed => "已完成",
            DownloadStatus.Failed => "下载失败",
            _ => "未知"
        };

        /// <summary>
        /// 任务创建时间
        /// </summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 创建时间格式化
        /// </summary>
        [Ignore]
        public string CreateTimeString => CreateTime.ToString("yyyy-MM-dd HH:mm:ss");

        /// <summary>
        /// 获取或设置下载开始时间。
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 获取或设置下载完成时间。
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 获取下载耗时。
        /// </summary>
        [Ignore]
        public TimeSpan? Duration => StartTime.HasValue && EndTime.HasValue ? EndTime - StartTime : null;

        /// <summary>
        /// 用时（秒）
        /// </summary>
        public int? DurationSeconds => (int?)(Duration?.TotalSeconds);

        /// <summary>
        /// 下载耗时格式化
        /// </summary>
        [Ignore]
        public string DurationString => Duration switch
        {
            var d when d?.TotalDays >= 1 => $"{(int)d.Value.TotalDays} 天 {d.Value.Hours} 小时",
            var d when d?.TotalHours >= 1 => $"{(int)d.Value.TotalHours} 小时 {d.Value.Minutes} 分钟",
            var d when d?.TotalMinutes >= 1 => $"{(int)d.Value.TotalMinutes} 分钟 {d.Value.Seconds} 秒",
            var d => $"{(int)(d?.TotalSeconds ?? 0)} 秒"
        };

        /// <summary>
        /// 获取或设置下载速度（字节/秒）。
        /// </summary>
        public double Speed { get; set; }

        /// <summary>
        /// 格式化 Speed 为字符串最大值 GB/MB/KB/B，保留两位小数
        /// </summary>
        [Ignore]
        public string SpeedString => Speed switch
        {
            // 如果 <=0 返回 -
            var s when s <= 0 => "-",
            var s when s >= 1024 * 1024 * 1024 => $"{s / 1024 / 1024 / 1024:F2} GB/s",
            var s when s >= 1024 * 1024 => $"{s / 1024 / 1024:F2} MB/s",
            var s when s >= 1024 => $"{s / 1024:F2} KB/s",
            var s => $"{s:F2} B/s"
        };

        /// <summary>
        /// 阿里云盘 ID（备份盘/资源盘）
        /// </summary>
        public string AliyunDriveId { get; set; }

        /// <summary>
        /// 云盘存储 ID / 本地存储 ID
        /// </summary>
        public string StorageConfigId { get; set; }

        /// <summary>
        /// 作业 ID
        /// </summary>
        public string JobId { get; set; }

        /// <summary>
        /// 文件 ID / Key
        /// </summary>
        public string FileId { get; set; }

        /// <summary>
        /// 表示是否为加密文件
        /// </summary>
        public bool IsEncrypted { get; set; }

        /// <summary>
        /// 文件名加密
        /// </summary>
        public bool IsEncryptName { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// 是否为本地文件
        /// </summary>
        public bool IsLocalFile { get; set; }
    }

    /// <summary>
    /// 表示下载任务的请求。
    /// </summary>
    public class DownloadTaskRequest
    {
        public string Url { get; set; }

        public string FilePath { get; set; }

        public string FileName { get; set; }

        public string JobId { get; set; }

        public string FileId { get; set; }

        /// <summary>
        /// 是否为本地文件
        /// </summary>
        public bool IsLocalFile { get; set; }
    }

    /// <summary>
    /// 批量下载请求
    /// </summary>
    public class BatchDownloadRequest
    {
        /// <summary>
        /// 文件唯一标识或 keys
        /// </summary>
        public List<string> FileIds { get; set; } = new List<string>();

        public string JobId { get; set; }

        public string FilePath { get; set; }

        /// <summary>
        /// 是否为本地文件
        /// </summary>
        public bool IsLocalFile { get; set; }
    }

    /// <summary>
    /// 表示下载任务的状态。
    /// </summary>
    public enum DownloadStatus
    {
        Pending = 0,
        Downloading = 1,
        Paused = 2,
        Completed = 3,
        Failed = 4,
        Canceled = 5
    }
}