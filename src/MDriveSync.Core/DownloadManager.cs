using MDriveSync.Core.DB;
using MDriveSync.Core.IO;
using MDriveSync.Core.Models;
using MDriveSync.Core.Services;
using MDriveSync.Security;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MDriveSync.Core
{
    /// <summary>
    /// 管理下载任务的服务类。
    /// </summary>
    public class DownloadManager
    {
        private static readonly Lazy<DownloadManager> instance = new Lazy<DownloadManager>(() => new DownloadManager());

        public static DownloadManager Instance => instance.Value;

        private readonly ConcurrentDictionary<string, DownloadTask> downloadTasks;
        private readonly HttpClient httpClient;
        private SemaphoreSlim semaphore;

        // 默认下载目录
        private string defaultDownloadDir;

        // 最大并行下载数
        private int maxParallelDownloads = 3;

        // 下载速度限制（字节/秒）
        private int downloadSpeedLimit = 0;

        private DownloadManager()
        {
            downloadTasks = new ConcurrentDictionary<string, DownloadTask>();
            httpClient = new HttpClient();
            semaphore = new SemaphoreSlim(maxParallelDownloads); // 默认并行下载数为3

            // 设置下载目录
            defaultDownloadDir = LastDownloadPath();
            downloadSpeedLimit = 0;
        }

        /// <summary>
        /// 设置最新并行数和默认下载目录，2 个参数
        /// </summary>
        /// <param name="maxParallelDownloads"></param>
        /// <param name="dir"></param>
        public void SetSettings(DownloadManagerSetting setting)
        {
            downloadSpeedLimit = setting.DownloadSpeedLimit;

            // 设置默认下载目录
            defaultDownloadDir = setting.DefaultDownload;

            // 不存在则创建
            if (!Directory.Exists(defaultDownloadDir))
            {
                Directory.CreateDirectory(defaultDownloadDir);
            }

            // 设置最大并行下载数
            SetMaxParallelDownloads(setting.MaxParallelDownload);
        }

        /// <summary>
        /// 获取下载器配置
        /// </summary>
        /// <returns></returns>
        public DownloadManagerSetting GetSettings()
        {
            return new DownloadManagerSetting
            {
                DefaultDownload = defaultDownloadDir,
                MaxParallelDownload = maxParallelDownloads,
                DownloadSpeedLimit = downloadSpeedLimit,
            };
        }

        /// <summary>
        /// 设置最大并行下载数。
        /// </summary>
        /// <param name="maxParallel">最大并行下载数。</param>
        public void SetMaxParallelDownloads(int maxParallel)
        {
            if (maxParallel < 1)
            {
                maxParallel = 1;
            }

            if (maxParallel > 10)
            {
                maxParallel = 10;
            }

            // 如果相等，则直接返回
            if (maxParallelDownloads == maxParallel)
            {
                return;
            }

            maxParallelDownloads = maxParallel;

            lock (semaphore)
            {
                var difference = maxParallelDownloads - semaphore.CurrentCount;
                if (difference > 0)
                {
                    semaphore.Release(difference);
                }
                else
                {
                    for (int i = 0; i < -difference; i++)
                    {
                        // semaphore.Wait();
                        semaphore.WaitAsync();
                    }
                }
            }
        }

        /// <summary>
        /// 添加新的下载任务。
        /// </summary>
        /// <param name="url">文件的 URL。</param>
        /// <param name="filePath">保存文件的路径。</param>
        /// <returns>下载任务对象。</returns>
        public DownloadTask AddDownloadTask(string url,
            string filePath,
            string jobId,
            string fileId,
            string driveId,
            string aliyunDriveId,
            bool isEncrypt,
            bool isEncryptName)
        {
            var downloadTask = new DownloadTask
            {
                Id = Guid.NewGuid().ToString("N"),
                Url = url,
                FilePath = filePath,
                Status = DownloadStatus.Pending,
                JobId = jobId,
                FileId = fileId,
                DriveId = driveId,
                AliyunDriveId = aliyunDriveId,
                IsEncrypted = isEncrypt,
                IsEncryptName = isEncryptName,
            };

            if (downloadTasks.TryAdd(downloadTask.Id, downloadTask))
            {
                StartDownloadTask(downloadTask);
            }

            return downloadTask;
        }

        /// <summary>
        /// 暂停下载任务。
        /// </summary>
        /// <param name="taskId">下载任务的唯一标识符。</param>
        public void PauseDownloadTask(string taskId)
        {
            if (downloadTasks.TryGetValue(taskId, out var downloadTask))
            {
                downloadTask.Status = DownloadStatus.Paused;
            }
        }

        /// <summary>
        /// 继续下载任务
        /// </summary>
        /// <param name="taskId"></param>
        public void ResumeDownloadTask(string taskId)
        {
            if (downloadTasks.TryGetValue(taskId, out var downloadTask))
            {
                if (downloadTask.Status != DownloadStatus.Completed)
                {
                    downloadTask.Status = DownloadStatus.Downloading;
                    StartDownloadTask(downloadTask);
                }
            }
        }

        /// <summary>
        /// 获取最后一次下载的路径。
        /// </summary>
        public string LastDownloadPath()
        {
            if (downloadTasks.Count > 0)
            {
                var lastTask = downloadTasks.Values.OrderByDescending(c => c.Id).Last();
                if (lastTask.Status == DownloadStatus.Completed)
                {
                    return Path.GetDirectoryName(lastTask.FilePath);
                }
            }

            // 根据平台类型返回特定的文件夹
            if (Platform.IsClientWindows)
            {
                // Windows平台的特殊文件夹
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            else
            {
                // 非Windows平台的特殊文件夹
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
        }

        /// <summary>
        /// 打开下载目录文件夹，并选中当前下载的文件
        /// </summary>
        /// <param name="taskId"></param>
        public void OpenDownloadFolder(string taskId)
        {
            if (downloadTasks.TryGetValue(taskId, out var downloadTask))
            {
                if (downloadTask.Status == DownloadStatus.Completed)
                {
                    // 根据平台类型打开文件夹
                    if (Platform.IsClientWindows)
                    {
                        // 如果下载完成，则打开文件夹并选中文件
                        Process.Start("explorer.exe", "/select," + downloadTask.FilePath);
                    }
                    else
                    {
                        Process.Start("open", "-R " + downloadTask.FilePath);
                    }
                }
                else
                {
                    // 打开对应的文件夹，不选中文件
                    if (Platform.IsClientWindows)
                    {
                        Process.Start("explorer.exe", "/select," + Path.GetDirectoryName(downloadTask.FilePath));
                    }
                    else
                    {
                        Process.Start("open", "-R " + Path.GetDirectoryName(downloadTask.FilePath));
                    }
                }
            }
        }

        /// <summary>
        /// 移除下载任务。
        /// </summary>
        /// <param name="taskId">下载任务的唯一标识符。</param>
        public void RemoveDownloadTask(string taskId)
        {
            downloadTasks.TryRemove(taskId, out _);
        }

        /// <summary>
        /// 删除已下载的文件。
        /// </summary>
        /// <param name="taskId">下载任务的唯一标识符。</param>
        public void RemoveFile(string taskId)
        {
            if (downloadTasks.TryGetValue(taskId, out var downloadTask))
            {
                if (File.Exists(downloadTask.FilePath))
                {
                    File.Delete(downloadTask.FilePath);
                }
            }
        }

        /// <summary>
        /// 获取所有下载任务。
        /// </summary>
        /// <returns>下载任务字典。</returns>
        public ConcurrentDictionary<string, DownloadTask> GetDownloadTasks()
        {
            return downloadTasks;
        }

        /// <summary>
        /// 获取全局下载速度（字节/秒）。
        /// </summary>
        /// <returns>全局下载速度。</returns>
        public double GetGlobalDownloadSpeed()
        {
            double totalSpeed = 0;

            foreach (var task in downloadTasks.Values)
            {
                if (task.Status == DownloadStatus.Downloading)
                {
                    totalSpeed += task.Speed;
                }
            }

            return totalSpeed;
        }

        /// <summary>
        /// 开始下载任务。
        /// </summary>
        /// <param name="downloadTask">下载任务对象。</param>
        private async void StartDownloadTask(DownloadTask downloadTask)
        {
            await semaphore.WaitAsync();

            var tmpFilePath = downloadTask.FilePath + $".{Guid.NewGuid():N}.cache";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(tmpFilePath));

                // 如果 url 为空或作业创建超过 5 分钟，则重新获取 url
                if (string.IsNullOrWhiteSpace(downloadTask.Url) || (DateTime.Now - downloadTask.CreateTime).TotalMinutes > 5)
                {
                    var token = AliyunDriveToken.Instance.GetAccessToken(downloadTask.DriveId);
                    var api = new AliyunDriveApi();
                    var urlResponse = api.GetDownloadUrl(downloadTask.AliyunDriveId, downloadTask.FileId, token);
                    downloadTask.Url = urlResponse.Url;
                }

                downloadTask.Status = DownloadStatus.Downloading;
                downloadTask.StartTime = DateTime.Now;

                var response = await httpClient.GetAsync(downloadTask.Url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                downloadTask.TotalBytes = response.Content.Headers.ContentLength ?? 0;

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tmpFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[1024 * 1024];
                    int bytesRead;
                    var stopwatch = Stopwatch.StartNew();
                    var lastDownloadedBytes = 0L;

                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        if (downloadTask.Status == DownloadStatus.Paused)
                        {
                            break;
                        }

                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadTask.DownloadedBytes += bytesRead;

                        // 全局下载速度限制
                        if (downloadSpeedLimit > 0)
                        {
                            while (GetGlobalDownloadSpeed() > downloadSpeedLimit)
                            {
                                await Task.Delay(100);

                                downloadTask.Speed = (downloadTask.DownloadedBytes - lastDownloadedBytes) / stopwatch.Elapsed.TotalSeconds;

                                // 更新结束时间
                                downloadTask.EndTime = DateTime.Now;
                            }
                        }

                        // 计算下载速度
                        if (stopwatch.Elapsed.TotalSeconds > 1)
                        {
                            downloadTask.Speed = (downloadTask.DownloadedBytes - lastDownloadedBytes) / stopwatch.Elapsed.TotalSeconds;
                            lastDownloadedBytes = downloadTask.DownloadedBytes;
                            stopwatch.Restart();

                            // 更新结束时间
                            downloadTask.EndTime = DateTime.Now;
                        }
                    }
                }

                // 下载完成后，判断文件是否需要解密，如果需要解密，则解密文件
                if (downloadTask.IsEncrypted)
                {
                    var entryptCachePath = downloadTask.FilePath + $".{Guid.NewGuid():N}.encrypt.cache";

                    try
                    {
                        // 获取 job 配置
                        var job = DriveDb.Instacne.Get(downloadTask.DriveId)?.Jobs?.FirstOrDefault(x => x.Id == downloadTask.JobId);
                        if (job == null)
                        {
                            throw new LogicException("未找到指定的任务配置");
                        }

                        var fileName = Path.GetFileName(downloadTask.FilePath);

                        using (FileStream inputFileStream = new FileStream(tmpFilePath, FileMode.Open, FileAccess.Read))
                        {
                            using (FileStream outputFileStream = new FileStream(entryptCachePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            {
                                CompressionHelper.DecompressStream(inputFileStream, outputFileStream,
                                    job.CompressAlgorithm, job.EncryptAlgorithm, job.EncryptKey, job.HashAlgorithm, job.IsEncryptName, out var decryptFileName);

                                if (!string.IsNullOrWhiteSpace(decryptFileName))
                                {
                                    fileName = decryptFileName;
                                }
                            }
                        }

                        // 重命名，并判断是否存在重复的文件，如果存在则继续重命名
                        var newFilePath = Path.Combine(Path.GetDirectoryName(downloadTask.FilePath), fileName);
                        if (File.Exists(newFilePath))
                        {
                            var newFileName = Path.GetFileNameWithoutExtension(fileName);
                            var newFileExtension = Path.GetExtension(fileName);
                            var i = 1;

                            do
                            {
                                newFilePath = Path.Combine(Path.GetDirectoryName(downloadTask.FilePath), $"{newFileName} ({i}){newFileExtension}");
                                i++;
                            } while (File.Exists(newFilePath));
                        }
                        File.Move(entryptCachePath, newFilePath);
                    }
                    catch (Exception ex)
                    {
                        throw new LogicException("解密失败：" + ex.Message);
                    }
                    finally
                    {
                        // 删除临时文件
                        if (File.Exists(tmpFilePath))
                        {
                            File.Delete(tmpFilePath);
                        }

                        // 删除解密临时文件
                        if (File.Exists(entryptCachePath))
                        {
                            File.Delete(entryptCachePath);
                        }
                    }
                }
                else
                {
                    var fileName = Path.GetFileName(downloadTask.FilePath);

                    // 重命名，并判断是否存在重复的文件，如果存在则继续重命名
                    var newFilePath = Path.Combine(Path.GetDirectoryName(downloadTask.FilePath), fileName);
                    if (File.Exists(newFilePath))
                    {
                        var newFileName = Path.GetFileNameWithoutExtension(fileName);
                        var newFileExtension = Path.GetExtension(fileName);
                        var i = 1;

                        do
                        {
                            newFilePath = Path.Combine(Path.GetDirectoryName(downloadTask.FilePath), $"{newFileName} ({i}){newFileExtension}");
                            i++;
                        } while (File.Exists(newFilePath));
                    }
                    File.Move(tmpFilePath, newFilePath);
                }


                // TODO
                // 回复文件读写权限、时间戳等
                // 判断文件状态，判断文件 sha1

                if (downloadTask.Status == DownloadStatus.Downloading)
                {
                    downloadTask.Status = DownloadStatus.Completed;
                    downloadTask.EndTime = DateTime.Now;

                    // 如果已完成，则计算平局速度
                    downloadTask.Speed = downloadTask.DownloadedBytes / downloadTask.Duration.Value.TotalSeconds;
                }
            }
            catch (Exception ex)
            {
                downloadTask.Status = DownloadStatus.Failed;
                downloadTask.Error = ex.Message;
            }
            finally
            {
                // 删除临时文件
                if (File.Exists(tmpFilePath))
                {
                    File.Delete(tmpFilePath);
                }

                semaphore.Release();

                // 检查任务数是否超过 1000+，如果超过则删除最早已完成的任务
                if (downloadTasks.Count > 1000)
                {
                    var earliestCompletedTasks = downloadTasks.Values.Where(x => x.Status == DownloadStatus.Completed).OrderBy(x => x.EndTime).Skip(1000).ToList();
                    foreach (var item in earliestCompletedTasks)
                    {
                        downloadTasks.TryRemove(item.Id, out _);
                    }
                }
            }
        }
    }
}