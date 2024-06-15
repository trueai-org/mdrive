using MDriveSync.Core;
using MDriveSync.Core.DB;
using MDriveSync.Core.IO;
using MDriveSync.Core.Models;
using MDriveSync.Core.ViewModels;
using MDriveSync.Infrastructure;
using MDriveSync.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using System.Text;

namespace MDriveSync.Client.API.Controllers
{
    /// <summary>
    /// 云盘作业控制器
    /// </summary>
    [Route("api/drive")]
    [ApiController]
    public class DriveController : ControllerBase
    {
        private readonly AliyunDriveHostedService _timedHostedService;

        public DriveController(AliyunDriveHostedService timedHostedService)
        {
            _timedHostedService = timedHostedService;
        }

        /// <summary>
        /// 常用的表达式
        /// </summary>
        /// <returns></returns>
        [HttpGet("crons")]
        public List<string> GetExpressions()
        {
            return QuartzCronScheduler.CommonExpressions.Keys.ToList();
        }

        /// <summary>
        /// 获取云盘配置
        /// </summary>
        /// <returns></returns>
        [HttpGet("drives")]
        public List<AliyunDriveConfig> GetDrives()
        {
            return _timedHostedService.Drives();
        }

        /// <summary>
        /// 导出配置为 json 文件
        /// </summary>
        /// <returns></returns>
        [HttpGet("export")]
        public IActionResult Export()
        {
            var aliyunDriveConfigs = AliyunDriveDb.Instance.DB.GetAll();
            var localJobConfigs = LocalStorageDb.Instance.DB.GetAll();

            var config = new ClientOptions
            {
                AliyunDrives = aliyunDriveConfigs,
                LocalStorages = localJobConfigs
            };

            // 导出的文件支持中文，需要使用 UTF-8 编码
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            var bytes = Encoding.UTF8.GetBytes(json);

            return File(bytes, "application/json", "mdrive.json");
        }

        /// <summary>
        /// 导入配置文件
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        [HttpPost("import")]
        public Result Import([FromForm] IFormFile file)
        {
            if (file == null)
            {
                return Result.Fail("文件不能为空");
            }

            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            var config = JsonConvert.DeserializeObject<ClientOptions>(json);

            // 阿里云配置导入处理
            var aliyunDrives = AliyunDriveDb.Instance.DB.GetAll();
            foreach (var cd in config.AliyunDrives)
            {
                var f = aliyunDrives.FirstOrDefault(x => x.Id == cd.Id);
                if (f == null)
                {
                    AliyunDriveDb.Instance.DB.Add(cd);
                }
                else
                {
                    // 循环配置是否存在，不存在则添加
                    var addCount = 0;
                    foreach (var job in cd.Jobs)
                    {
                        var j = f.Jobs.FirstOrDefault(x => x.Id == job.Id);
                        if (j == null)
                        {
                            f.Jobs.Add(job);
                            addCount++;
                        }
                    }

                    if (addCount > 0)
                    {
                        AliyunDriveDb.Instance.DB.Update(f);
                    }
                }
            }

            // 本地存储配置导入处理
            var localStorages = LocalStorageDb.Instance.DB.GetAll();
            foreach (var cd in config.LocalStorages)
            {
                var f = localStorages.FirstOrDefault(x => x.Id == cd.Id);
                if (f == null)
                {
                    LocalStorageDb.Instance.DB.Add(cd);
                }
                else
                {
                    // 循环配置是否存在，不存在则添加
                    var addCount = 0;
                    foreach (var job in cd.Jobs)
                    {
                        var j = f.Jobs.FirstOrDefault(x => x.Id == job.Id);
                        if (j == null)
                        {
                            f.Jobs.Add(job);
                            addCount++;
                        }
                    }

                    if (addCount > 0)
                    {
                        LocalStorageDb.Instance.DB.Update(f);
                    }
                }
            }

            return Result.Ok();
        }

        /// <summary>
        /// 挂载磁盘 - 云盘挂载
        /// </summary>
        /// <returns></returns>
        [HttpPost("mount/{driveId}")]
        public Result DriveMount(string driveId)
        {
            _timedHostedService.DriveMount(driveId);
            return Result.Ok();
        }

        /// <summary>
        /// 挂载磁盘 - 云盘卸载
        /// </summary>
        /// <param name="driveId"></param>
        /// <returns></returns>
        [HttpPost("unmount/{driveId}")]
        public Result DriveUnmount(string driveId)
        {
            _timedHostedService.DriveUnmount(driveId);
            return Result.Ok();
        }

        /// <summary>
        /// 挂载磁盘 - 云盘作业挂载
        /// </summary>
        /// <param name="mountRequest"></param>
        /// <returns></returns>
        [HttpPost("job/mount/{jobId}")]
        public Result DriveJobMount(string jobId)
        {
            _timedHostedService.DriveJobMount(jobId);
            return Result.Ok();
        }

        /// <summary>
        /// 挂载磁盘 - 云盘作业卸载
        /// </summary>
        /// <param name="driveId"></param>
        /// <returns></returns>
        [HttpPost("job/unmount/{jobId}")]
        public Result DriveJobUnmount(string jobId)
        {
            _timedHostedService.DriveJobUnmount(jobId);
            return Result.Ok();
        }

        /// <summary>
        /// 获取作业
        /// </summary>
        /// <returns></returns>
        [HttpGet("jobs")]
        public Result<List<AliyunJobConfig>> GetJobs()
        {
            var data = _timedHostedService.Jobs().Values
                .ToList()
                .Select(c =>
                {
                    var j = c.CurrrentJob.GetClone();
                    j.State = c.CurrentState;
                    j.Metadata ??= new();
                    j.Metadata.Message = c.ProcessMessage;
                    j.IsMount = c.DriveIsMount();

                    return j;
                }).ToList();
            return Result.Ok(data);
        }

        /// <summary>
        /// 获取云盘文件文件夹
        /// </summary>
        /// <param name="jobId"></param>
        /// <returns></returns>
        [HttpGet("files/{jobId}")]
        public async Task<List<AliyunDriveFileItem>> GetDrivleFiles(string jobId, [FromQuery] string parentId = "")
        {
            var jobs = _timedHostedService.Jobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                return await job.GetDrivleFiles(parentId);
            }
            return new List<AliyunDriveFileItem>();
        }

        /// <summary>
        /// 获取云盘文件下载链接
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="fileId"></param>
        /// <returns></returns>
        [HttpGet("download/{jobId}/{fileId}")]
        public AliyunDriveOpenFileGetDownloadUrlResponse GetDownloadUrl(string jobId, string fileId)
        {
            var jobs = _timedHostedService.Jobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                return job.AliyunDriveGetDownloadUrl(fileId);
            }
            return null;
        }

        /// <summary>
        /// 下载文件V3 - 支持加密文件下载（返回下载目录和Url）
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="fileId"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpGet("download-v3/{jobId}/{fileId}")]
        public async Task<Result> DownloadV3(string jobId, string fileId)
        {
            var jobs = _timedHostedService.Jobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                var detail = job.GetFileDetail(fileId);
                var urlResponse = job.AliyunDriveGetDownloadUrl(fileId);

                // 如果是加密文件，需要解密后再下载
                var url = urlResponse.Url;
                var name = detail.Name;

                if (job.CurrrentJob.IsEncrypt)
                {
                    if (job.CurrrentJob.IsEncryptName)
                    {
                        var jobConfig = job.CurrrentJob;
                        var httpClient = new HttpClient();

                        // 设置Range头以只下载0到1112字节
                        var request = new HttpRequestMessage(HttpMethod.Get, url);
                        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, Math.Min(1112, detail.Size ?? 0));

                        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new LogicException("无法下载文件");
                        }

                        // 直接从 HttpResponseMessage 获取流
                        var inputStream = await response.Content.ReadAsStreamAsync();

                        // 解密流
                        var outputStream = new MemoryStream();

                        // 解密流
                        CompressionHelper.DecompressStream(inputStream, outputStream, jobConfig.CompressAlgorithm, jobConfig.EncryptAlgorithm,
                            jobConfig.EncryptKey, jobConfig.HashAlgorithm, jobConfig.IsEncryptName, out var decryptFileName, true);

                        outputStream.Seek(0, SeekOrigin.Begin);

                        if (!string.IsNullOrWhiteSpace(decryptFileName))
                        {
                            name = decryptFileName;
                        }
                    }
                    else
                    {
                        name = name.TrimSuffix(".e");
                    }
                }

                // 返回默认的下载链接和上次的下载位置（如果没有下载，则返回默认下载地址）
                return Result.Ok(new
                {
                    Url = url,
                    DownloadPath = DownloadManager.Instance.GetSettings().DefaultDownload ?? DownloadManager.Instance.LastDownloadPath(),
                    FileName = name,
                    FileId = fileId,
                    JobId = jobId
                });
            }

            return Result.Fail("作业不存在");
        }

        /////// <summary>
        /////// 下载 V2 - 测试（在线下载方式，占用内存过大）
        /////// </summary>
        /////// <param name="jobId"></param>
        /////// <param name="fileId"></param>
        /////// <returns></returns>
        ////[HttpGet("download-v2/{jobId}/{fileId}")]
        ////public async Task<IActionResult> DownloadV2(string jobId, string fileId)
        ////{
        ////    var jobs = _timedHostedService.Jobs();
        ////    if (jobs.TryGetValue(jobId, out var job) && job != null)
        ////    {
        ////        var detail = job.GetFileDetail(fileId);
        ////        var urlResponse = job.AliyunDriveGetDownloadUrl(fileId);

        ////        // 如果是加密文件，需要解密后再下载
        ////        var url = urlResponse.Url;

        ////        DownloadManager.Instance.AddDownloadTask(url, @$"E:\test2\${detail.Name}");

        ////        if (job.CurrrentJob.IsEncrypt)
        ////        {
        ////            return Ok();

        ////            var jobConfig = job.CurrrentJob;

        ////            var httpClient = new HttpClient();
        ////            var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        ////            if (!response.IsSuccessStatusCode)
        ////            {
        ////                return BadRequest("无法下载文件");
        ////            }

        ////            // 下载文件到本地临时文件
        ////            var tempFilePath = Path.GetTempFileName();
        ////            using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        ////            {
        ////                await response.Content.CopyToAsync(fileStream);
        ////            }

        ////            var fileDownloadName = detail.Name;

        ////            // 解密和解压缩文件到另一个临时文件
        ////            var decryptedFilePath = Path.GetTempFileName();
        ////            using (var inputFileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
        ////            using (var outputFileStream = new FileStream(decryptedFilePath, FileMode.Create, FileAccess.Write))
        ////            {
        ////                CompressionHelper.DecompressStream(inputFileStream, outputFileStream, jobConfig.CompressAlgorithm, jobConfig.EncryptAlgorithm,
        ////                    jobConfig.EncryptKey, jobConfig.HashAlgorithm, jobConfig.IsEncryptName, out fileDownloadName);
        ////            }

        ////            System.IO.File.Delete(tempFilePath); // 删除加密的临时文件

        ////            // 设置文件名和内容类型
        ////            //var fileName = !string.IsNullOrEmpty(decryptFileName) ? detail.Name : Path.GetFileName(decryptedFilePath);
        ////            var contentDisposition = new ContentDispositionHeaderValue("attachment")
        ////            {
        ////                FileName = fileDownloadName ?? detail.Name
        ////            };
        ////            Response.Headers[HeaderNames.ContentDisposition] = contentDisposition.ToString();

        ////            var mimeType = "application/octet-stream";
        ////            Response.Headers[HeaderNames.ContentType] = mimeType;

        ////            var decryptedStream = new FileStream(decryptedFilePath, FileMode.Open, FileAccess.Read, FileShare.Delete);
        ////            return new FileStreamResult(decryptedStream, mimeType)
        ////            {
        ////                FileDownloadName = fileDownloadName ?? detail.Name
        ////            };
        ////        }
        ////        else
        ////        {
        ////            var httpClient = new HttpClient();
        ////            var response = await httpClient.GetAsync(urlResponse.Url, HttpCompletionOption.ResponseHeadersRead);

        ////            if (!response.IsSuccessStatusCode)
        ////            {
        ////                return BadRequest("无法下载文件");
        ////            }

        ////            var stream = await response.Content.ReadAsStreamAsync();

        ////            var contentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = detail.Name };
        ////            Response.Headers.Append("Content-Disposition", contentDisposition.ToString());

        ////            var mimeType = "application/octet-stream";
        ////            return new FileStreamResult(stream, mimeType);
        ////        }
        ////    }

        ////    return NotFound("Job or file not found.");
        ////}

        /////// <summary>
        /////// 下载文件V2 - 支持加密文件下载（边下载边解压/解密）
        /////// </summary>
        /////// <param name="jobId"></param>
        /////// <param name="fileId"></param>
        /////// <returns></returns>
        ////[HttpGet("download-v2/{jobId}/{fileId}")]
        ////public async Task<IActionResult> DownloadV2(string jobId, string fileId)
        ////{
        ////    var jobs = _timedHostedService.Jobs();
        ////    if (jobs.TryGetValue(jobId, out var job) && job != null)
        ////    {
        ////        var detail = job.GetFileDetail(fileId);
        ////        var urlResponse = job.AliyunDriveGetDownloadUrl(fileId);

        ////        if (job.CurrrentJob.IsEncrypt)
        ////        {
        ////            // 如果是加密文件，需要解密后再下载
        ////            var url = urlResponse.Url;
        ////            var jobConfig = job.CurrrentJob;

        ////            //// 解密测试
        ////            //using FileStream inputFileStream1 = new FileStream(encryptCacheFile, FileMode.Open, FileAccess.Read);
        ////            //using FileStream outputFileStream1 = new FileStream(Path.Combine(encryptCachePath, $"{Guid.NewGuid():N}.d.cache"), FileMode.Create, FileAccess.Write);
        ////            //CompressionHelper.DecompressStream(inputFileStream1, outputFileStream1, _jobConfig.CompressAlgorithm, _jobConfig.EncryptAlgorithm, _jobConfig.EncryptKey, _jobConfig.HashAlgorithm,
        ////            //    _jobConfig.IsEncryptName, out var decryptFileName);
        ////            //inputFileStream1.Close();
        ////            //outputFileStream1.Close();

        ////            // 通过 url 下载文件流，并滚动解密
        ////            var httpClient = new HttpClient();
        ////            var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        ////            if (!response.IsSuccessStatusCode)
        ////            {
        ////                return BadRequest("无法下载文件");
        ////            }

        ////            // 直接从 HttpResponseMessage 获取流
        ////            var inputStream = await response.Content.ReadAsStreamAsync();

        ////            // 使用管道处理解密和解压数据流
        ////            var pipe = new Pipe();
        ////            _ = Task.Run(async () =>
        ////            {
        ////                try
        ////                {
        ////                    await DecryptAndDecompressStreamAsync(inputStream, pipe.Writer, jobConfig);
        ////                }
        ////                catch (Exception ex)
        ////                {
        ////                    pipe.Writer.Complete(ex);
        ////                }
        ////            });

        ////            // 设置文件名和内容类型
        ////            var fileName = detail.Name;
        ////            if (!string.IsNullOrEmpty(fileName))
        ////            {
        ////                var contentDisposition = new ContentDispositionHeaderValue("attachment")
        ////                {
        ////                    FileName = fileName
        ////                };
        ////                Response.Headers[HeaderNames.ContentDisposition] = contentDisposition.ToString();
        ////            }

        ////            var mimeType = "application/octet-stream";
        ////            Response.Headers[HeaderNames.ContentType] = mimeType;

        ////            return new FileStreamResult(pipe.Reader.AsStream(), mimeType);

        ////            //// 设置文件名和内容类型
        ////            //var contentDisposition = new ContentDispositionHeaderValue("attachment")
        ////            //{
        ////            //    FileName = detail.Name
        ////            //};
        ////            //Response.Headers[HeaderNames.ContentDisposition] = contentDisposition.ToString();

        ////            //var mimeType = "application/octet-stream";
        ////            //Response.Headers[HeaderNames.ContentType] = mimeType;

        ////            //// 使用自定义的 DecryptAndDecompressStream 方法
        ////            //await DecryptAndDecompressStreamAsync(inputStream, Response.Body, jobConfig);

        ////            //// 临时缓冲区大小
        ////            //const int bufferSize = 16 * 1024 * 1024;

        ////            //// 创建一个临时文件来存储解密和解压缩后的数据
        ////            //using (var tempOutputStream = new MemoryStream())
        ////            //{
        ////            //    CompressionHelper.DecompressStream(inputStream, tempOutputStream, jobConfig.CompressAlgorithm, jobConfig.EncryptAlgorithm,
        ////            //        jobConfig.EncryptKey, jobConfig.HashAlgorithm, jobConfig.IsEncryptName, out var fileName);

        ////            //    // 将解密解压后的数据逐块写入响应流
        ////            //    tempOutputStream.Seek(0, SeekOrigin.Begin);

        ////            //    // 如果解密到文件名
        ////            //    if (!string.IsNullOrWhiteSpace(fileName))
        ////            //    {
        ////            //        var contentDisposition2 = new ContentDispositionHeaderValue("attachment") { FileName = fileName };
        ////            //        Response.Headers[HeaderNames.ContentDisposition] = contentDisposition2.ToString();
        ////            //    }

        ////            //    await tempOutputStream.CopyToAsync(Response.Body, bufferSize);
        ////            //}

        ////            //return new EmptyResult(); // 请求已经处理完毕

        ////            ////// 启动解密和解压的任务
        ////            ////await DecryptAndDecompressStreamAsync(pipeReader, pipeWriter, jobConfig);

        ////            ////return new EmptyResult(); // 请求已经处理完毕

        ////            //// 解密流
        ////            //var outputStream = new MemoryStream();

        ////            //// 解密流
        ////            //CompressionHelper.DecompressStream(inputStream, outputStream, jobConfig.CompressAlgorithm, jobConfig.EncryptAlgorithm,
        ////            //    jobConfig.EncryptKey, jobConfig.HashAlgorithm, jobConfig.IsEncryptName, out var decryptFileName);

        ////            //outputStream.Seek(0, SeekOrigin.Begin);

        ////            ////// 设置为分块传输
        ////            ////Response.Headers.Append("Transfer-Encoding", "chunked");

        ////            ////// 设置文件名和内容类型
        ////            ////var contentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = decryptFileName ?? detail.Name };
        ////            ////Response.Headers.Append("Content-Disposition", contentDisposition.ToString());

        ////            ////var mimeType = "application/octet-stream";

        ////            //// 返回 FileStreamResult，使用解密和解压后的流
        ////            //return new FileStreamResult(outputStream, mimeType);
        ////        }
        ////        else
        ////        {
        ////            // 使用 HttpClient 发送请求
        ////            var httpClient = new HttpClient();
        ////            var response = await httpClient.GetAsync(urlResponse.Url, HttpCompletionOption.ResponseHeadersRead);

        ////            if (!response.IsSuccessStatusCode)
        ////            {
        ////                return BadRequest("无法下载文件");
        ////            }

        ////            // 直接从 HttpResponseMessage 获取流
        ////            var stream = await response.Content.ReadAsStreamAsync();

        ////            // 设置为分块传输
        ////            //Response.Headers.Append("Transfer-Encoding", "chunked");

        ////            // 设置文件名和内容类型
        ////            var contentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = detail.Name };
        ////            Response.Headers.Append("Content-Disposition", contentDisposition.ToString());

        ////            var mimeType = "application/octet-stream";
        ////            //var extension = Path.GetExtension(name).ToLowerInvariant();
        ////            //switch (extension)
        ////            //{
        ////            //    case ".mp3":
        ////            //        mimeType = "audio/mpeg";
        ////            //        break;
        ////            //    case ".mp4":
        ////            //        mimeType = "video/mp4";
        ////            //        break;
        ////            //    case ".jpg":
        ////            //    case ".jpeg":
        ////            //        mimeType = "image/jpeg";
        ////            //        break;
        ////            //    case ".png":
        ////            //        mimeType = "image/png";
        ////            //        break;
        ////            //    default:
        ////            //        break;
        ////            //}

        ////            // 返回 FileStreamResult，使用原始流
        ////            return new FileStreamResult(stream, mimeType);
        ////        }
        ////    }

        ////    return NotFound("Job or file not found.");

        ////    //var jobs = _timedHostedService.Jobs();
        ////    //if (jobs.TryGetValue(jobId, out var job) && job != null)
        ////    //{
        ////    //    var detail = job.GetFileDetail(fileId);
        ////    //    var urlResponse = job.AliyunDriveGetDownloadUrl(fileId);
        ////    //    var url = urlResponse.Url;
        ////    //    var jobConfig = job.CurrrentJob;

        ////    //    var httpClient = new HttpClient();
        ////    //    var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        ////    //    if (!response.IsSuccessStatusCode)
        ////    //    {
        ////    //        return BadRequest("无法下载文件");
        ////    //    }

        ////    //    var inputStream = await response.Content.ReadAsStreamAsync();

        ////    //    // 创建一个管道流，用于异步解密和解压数据
        ////    //    var pipeStream = new System.IO.Pipelines.Pipe();

        ////    //    if (jobConfig.IsEncrypt)
        ////    //    {
        ////    //        _ = Task.Run(async () =>
        ////    //        {
        ////    //            try
        ////    //            {
        ////    //                await using (var outputStream = pipeStream.Writer.AsStream())
        ////    //                {
        ////    //                    CompressionHelper.DecompressStream(inputStream, outputStream, jobConfig.CompressAlgorithm, jobConfig.EncryptAlgorithm,
        ////    //                        jobConfig.EncryptKey, jobConfig.HashAlgorithm, jobConfig.IsEncryptName, out var decryptedFileName);
        ////    //                }
        ////    //                pipeStream.Writer.Complete();
        ////    //            }
        ////    //            catch (Exception ex)
        ////    //            {
        ////    //                pipeStream.Writer.Complete(ex);
        ////    //            }
        ////    //        });
        ////    //    }
        ////    //    else
        ////    //    {
        ////    //        _ = Task.Run(async () =>
        ////    //        {
        ////    //            await using (var outputStream = pipeStream.Writer.AsStream())
        ////    //            {
        ////    //                await inputStream.CopyToAsync(outputStream);
        ////    //            }
        ////    //            pipeStream.Writer.Complete();
        ////    //        });
        ////    //    }

        ////    //    // 设置文件名和内容类型
        ////    //    var contentDisposition = new ContentDispositionHeaderValue("attachment")
        ////    //    {
        ////    //        FileName = jobConfig.IsEncrypt ? detail.EncryptedName : detail.Name
        ////    //    };
        ////    //    Response.Headers[HeaderNames.ContentDisposition] = contentDisposition.ToString();

        ////    //    var mimeType = "application/octet-stream";
        ////    //    Response.Headers[HeaderNames.ContentType] = mimeType;

        ////    //    return new FileStreamResult(pipeStream.Reader.AsStream(), mimeType);
        ////    //}

        ////    //return NotFound("Job or file not found.");
        ////}

        ////private async Task DecryptAndDecompressStreamAsync(PipeReader reader, PipeWriter writer, JobConfig jobConfig)
        ////{
        ////    while (true)
        ////    {
        ////        ReadResult result = await reader.ReadAsync();
        ////        ReadOnlySequence<byte> buffer = result.Buffer;

        ////        if (buffer.Length == 0 && result.IsCompleted)
        ////        {
        ////            break;
        ////        }

        ////        // 对数据进行解密和解压处理
        ////        foreach (var segment in buffer)
        ////        {
        ////            byte[] decryptedData = CompressionHelper.Decompress(segment.ToArray(), jobConfig.CompressAlgorithm, jobConfig.EncryptAlgorithm, jobConfig.EncryptKey);
        ////            await writer.WriteAsync(decryptedData);
        ////        }

        ////        reader.AdvanceTo(buffer.End);
        ////    }

        ////    await writer.CompleteAsync();
        ////}

        //private async Task DecryptAndDecompressStreamAsync(Stream inputStream, PipeWriter writer, JobConfig jobConfig)
        //{
        //    using (var tempOutputStream = new MemoryStream())
        //    {
        //        CompressionHelper.DecompressStream(inputStream, tempOutputStream, jobConfig.CompressAlgorithm, jobConfig.EncryptAlgorithm,
        //            jobConfig.EncryptKey, jobConfig.HashAlgorithm, jobConfig.IsEncryptName, out var fileName);

        //        // 将解密解压后的数据逐块写入响应流
        //        tempOutputStream.Seek(0, SeekOrigin.Begin);

        //        const int bufferSize = 16 * 1024 * 1024; // 16 MB buffer
        //        var buffer = new byte[bufferSize];
        //        int bytesRead;
        //        while ((bytesRead = await tempOutputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        //        {
        //            await writer.WriteAsync(buffer.AsMemory(0, bytesRead));
        //        }
        //    }

        //    await writer.CompleteAsync();
        //}

        //private async Task DecryptAndDecompressStreamAsync(Stream inputStream, Stream outputStream, JobConfig jobConfig)
        //{
        //    // 临时缓冲区大小
        //    const int bufferSize = 16 * 1024 * 1024;

        //    // 创建一个临时文件来存储解密和解压缩后的数据
        //    using (var tempOutputStream = new MemoryStream())
        //    {
        //        CompressionHelper.DecompressStream(inputStream, tempOutputStream, jobConfig.CompressAlgorithm, jobConfig.EncryptAlgorithm,
        //            jobConfig.EncryptKey, jobConfig.HashAlgorithm, jobConfig.IsEncryptName, out var fileName);

        //        // 将解密解压后的数据逐块写入响应流
        //        tempOutputStream.Seek(0, SeekOrigin.Begin);

        //        await tempOutputStream.CopyToAsync(outputStream, bufferSize);
        //    }
        //}

        /// <summary>
        /// 获取文件详情
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="fileId"></param>
        /// <returns></returns>
        [HttpGet("file/{jobId}/{fileId}")]
        public FilePathKeyResult GetDetail(string jobId, string fileId)
        {
            var jobs = _timedHostedService.Jobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                return job.GetFileDetail(fileId);
            }
            return null;
        }

        /// <summary>
        /// 云盘添加
        /// </summary>
        /// <param name="cfg"></param>
        /// <returns></returns>
        [HttpPost()]
        public Result DriveAdd([FromBody] DriveEditRequest cfg)
        {
            _timedHostedService.DriveAdd(cfg);
            return Result.Ok();
        }

        /// <summary>
        /// 云盘编辑
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="cfg"></param>
        /// <returns></returns>
        [HttpPut("{driveId}")]
        public Result DriveEdit(string driveId, [FromBody] DriveEditRequest cfg)
        {
            _timedHostedService.DriveEdit(driveId, cfg);
            return Result.Ok();
        }

        /// <summary>
        /// 云盘删除
        /// </summary>
        /// <param name="driveId"></param>
        /// <returns></returns>
        [HttpDelete("{driveId}")]
        public Result DriveDelete(string driveId)
        {
            _timedHostedService.DriveDelete(driveId);
            return Result.Ok();
        }

        /// <summary>
        /// 更新作业配置（只有空闲、错误、取消、禁用、完成状态才可以更新）
        /// </summary>
        /// <param name="cfg"></param>
        [HttpPut("job")]
        public Result JobUpdate([FromBody] AliyunJobConfig cfg)
        {
            var jobs = _timedHostedService.Jobs();
            if (jobs.TryGetValue(cfg.Id, out var job) && job != null)
            {
                job.JobUpdate(cfg);
            }
            return Result.Ok();
        }

        /// <summary>
        /// 作业添加
        /// </summary>
        /// <param name="cfg"></param>
        /// <returns></returns>
        [HttpPost("job/{driveId}")]
        public Result JobAdd(string driveId, [FromBody] AliyunJobConfig cfg)
        {
            _timedHostedService.JobAdd(driveId, cfg);
            return Result.Ok();
        }

        /// <summary>
        /// 作业状态修改
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        [HttpPut("job/{jobId}/{state}")]
        public Result JobStateChange(string jobId, JobState state)
        {
            var jobs = _timedHostedService.Jobs();
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                job.JobStateChange(state);
            }

            // 如果作业不再执行队列中
            if (state == JobState.Deleted)
            {
                // 删除作业，清除服务
                _timedHostedService.JobDelete(jobId);

                // 修复 https://github.com/trueai-org/MDriveSync/issues/4
                var ds = AliyunDriveDb.Instance.DB.GetAll(false);
                foreach (var d in ds)
                {
                    var jo = d?.Jobs?.FirstOrDefault(x => x.Id == jobId);
                    if (jo != null)
                    {
                        d.Jobs.RemoveAll(x => x.Id == jobId);
                        AliyunDriveDb.Instance.DB.Update(d);
                    }
                }
            }

            return Result.Ok();
        }

        /// <summary>
        /// 获取文价夹/路径下拉列表
        /// </summary>
        /// <returns></returns>
        [HttpPost("paths")]
        public Result<List<TreeNode>> GetPaths([FromBody] TreeNodePathRequest request)
        {
            var data = Filesystem.TreeNodes(request.Path);
            return Result.Ok(data);
        }

        /// <summary>
        /// 获取可用于挂载的磁盘盘符或挂载点
        /// </summary>
        /// <returns></returns>
        [HttpGet("points")]
        public Result<List<string>> GetAvailableMountPoints()
        {
            var data = Filesystem.GetAvailableMountPoints().ToList();
            return Result.Ok(data);
        }

        /// <summary>
        /// 文件下载
        /// 直接流式传输的方法是一种高效处理大文件下载的方式。
        /// 这种方法通过直接将原始响应流传输到客户端，避免了将文件内容完全加载到服务器内存中。这样做既减少了内存消耗，也提高了处理大文件的效率。
        /// </summary>
        /// <param name="url"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        [HttpGet("download")]
        public async Task<IActionResult> DownloadFileAsync([FromQuery] string url, [FromQuery] string name)
        {
            // 使用 HttpClient 发送请求
            var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                return BadRequest("无法下载文件");
            }

            // 直接从 HttpResponseMessage 获取流
            var stream = await response.Content.ReadAsStreamAsync();

            // 设置为分块传输
            //Response.Headers.Append("Transfer-Encoding", "chunked");

            // 设置文件名和内容类型
            var contentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = name };
            Response.Headers.Append("Content-Disposition", contentDisposition.ToString());

            var mimeType = "application/octet-stream";
            //var extension = Path.GetExtension(name).ToLowerInvariant();
            //switch (extension)
            //{
            //    case ".mp3":
            //        mimeType = "audio/mpeg";
            //        break;
            //    case ".mp4":
            //        mimeType = "video/mp4";
            //        break;
            //    case ".jpg":
            //    case ".jpeg":
            //        mimeType = "image/jpeg";
            //        break;
            //    case ".png":
            //        mimeType = "image/png";
            //        break;
            //    default:
            //        break;
            //}

            // 返回 FileStreamResult，使用原始流
            return new FileStreamResult(stream, mimeType);
        }

        ///// <summary>
        ///// 文件下载
        ///// </summary>
        ///// <param name="fileUrl"></param>
        ///// <returns></returns>
        //[HttpGet("download")]
        //public async Task<IActionResult> DownloadFile([FromQuery] string url, [FromQuery] string name)
        //{
        //    using (var httpClient = new HttpClient())
        //    {
        //        var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        //        if (!response.IsSuccessStatusCode)
        //        {
        //            return BadRequest("无法下载文件");
        //        }

        //        var stream = await response.Content.ReadAsStreamAsync();
        //        var contentDisposition = new ContentDispositionHeaderValue("attachment")
        //        {
        //            FileName = name
        //        };

        //        Response.Headers.Append("Content-Disposition", contentDisposition.ToString());
        //        return File(stream, "application/octet-stream");
        //    }
        //}

        ///// <summary>
        ///// 使用分块传输和流式处理来优化大文件下载
        ///// </summary>
        ///// <param name="url"></param>
        ///// <param name="name"></param>
        ///// <returns></returns>
        //[HttpGet("download")]
        //public async Task<IActionResult> DownloadFileAsync([FromQuery] string url, [FromQuery] string name)
        //{
        //    var httpClient = new HttpClient();
        //    var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        //    if (!response.IsSuccessStatusCode)
        //    {
        //        return BadRequest("无法下载文件");
        //    }

        //    var stream = await response.Content.ReadAsStreamAsync();

        //    // 设置为分块传输
        //    Response.Headers.Append("Transfer-Encoding", "chunked");
        //    var contentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = name };
        //    Response.Headers.Append("Content-Disposition", contentDisposition.ToString());

        //    return new FileStreamResult(stream, "application/octet-stream");
        //}

        // ------------------------ 文件下载 -----------------

        /// <summary>
        /// 添加新的下载任务。
        /// </summary>
        /// <param name="url">文件的 URL。</param>
        /// <param name="filePath">保存文件的路径。</param>
        /// <returns>下载任务对象。</returns>
        [HttpPost("download-task")]
        public Result AddDownloadTask([FromBody] DownloadTaskRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.FilePath))
            {
                throw new LogicException("请选择保存位置");
            }

            var jobs = _timedHostedService.Jobs();
            var jobId = request.JobId;
            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                var downloadTask = DownloadManager.Instance.AddDownloadTask(request.Url, Path.Combine(request.FilePath, request.FileName), request.JobId, request.FileId,
                    job.CurrrentDrive.Id, job.AliyunDriveId, job.CurrrentJob.IsEncrypt, job.CurrrentJob.IsEncryptName);
                return Result.Ok(downloadTask);
            }

            return Result.Fail("作业不存在");
        }

        /// <summary>
        /// 添加批量下载任务。
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("download-tasks")]
        public Result AddDownloadTasks([FromBody] BatchDownloadRequest param)
        {
            var jobs = _timedHostedService.Jobs();
            var jobId = param.JobId;
            var baseSavePath = param.FilePath;

            if (string.IsNullOrWhiteSpace(baseSavePath))
            {
                throw new LogicException("请选择保存位置");
            }

            if (jobs.TryGetValue(jobId, out var job) && job != null)
            {
                Task.Run(() => DownloadManager.Instance.AddDownloadTasksAsync(param, job));

                //// 如果是文件夹，获取文件夹下的所有文件
                //foreach (var fileId in param.FileIds)
                //{
                //    var detail = job.GetFileDetail(fileId);
                //    if (detail.IsFolder)
                //    {
                //        var subPath = detail.Name;

                //        // 获取子文件夹下的所有文件
                //        await job.AliyunDriveFetchAllSubFiles(fileId);

                //        // 获取子文件夹下的所有文件
                //        var parentKey = job.DriveFolders.FirstOrDefault(x => x.Value.FileId == fileId).Key;
                //        if (!string.IsNullOrWhiteSpace(parentKey))
                //        {
                //            var fids = job.DriveFiles.Where(c => c.Value.IsFile && c.Key.StartsWith(parentKey)).Select(c => c.Value.FileId).ToList();

                //            foreach (var fid in fids)
                //            {
                //                var subDetail = job.GetFileDetail(fid);
                //                var subUrlResponse = job.AliyunDriveGetDownloadUrl(fid);

                //                // 如果是加密文件，需要解密后再下载
                //                var subUrl = subUrlResponse.Url;
                //                var subName = subDetail.Name;

                //                if (job.CurrrentJob.IsEncrypt && job.CurrrentJob.IsEncryptName)
                //                {
                //                    var jobConfig = job.CurrrentJob;
                //                    var httpClient = new HttpClient();

                //                    // 设置Range头以只下载0到1112字节
                //                    var request = new HttpRequestMessage(HttpMethod.Get, subUrl);
                //                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, Math.Min(1112, subDetail.Size ?? 0));

                //                    var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                //                    if (!response.IsSuccessStatusCode)
                //                    {
                //                        throw new LogicException("无法下载文件");
                //                    }

                //                    // 直接从 HttpResponseMessage 获取流
                //                    var inputStream = await response.Content.ReadAsStreamAsync();

                //                    // 解密流
                //                    var outputStream = new MemoryStream();

                //                    // 解密流
                //                    CompressionHelper.DecompressStream(inputStream, outputStream, jobConfig.CompressAlgorithm, jobConfig.EncryptAlgorithm,
                //                                                               jobConfig.EncryptKey, jobConfig.HashAlgorithm, jobConfig.IsEncryptName, out var decryptFileName, true);

                //                    outputStream.Seek(0, SeekOrigin.Begin);

                //                    if (!string.IsNullOrWhiteSpace(decryptFileName))
                //                    {
                //                        subName = decryptFileName;
                //                    }
                //                }

                //                var path = Path.Combine(baseSavePath, subPath, subName);
                //                DownloadManager.Instance.AddDownloadTask(subUrl, path, jobId, fid, job.CurrrentDrive.Id, job.AliyunDriveId, job.CurrrentJob.IsEncrypt, job.CurrrentJob.IsEncryptName);
                //            }
                //        }
                //    }
                //    else
                //    {
                //        var urlResponse = job.AliyunDriveGetDownloadUrl(fileId);

                //        // 如果是加密文件，需要解密后再下载
                //        var url = urlResponse.Url;
                //        var name = detail.Name;

                //        if (job.CurrrentJob.IsEncrypt && job.CurrrentJob.IsEncryptName)
                //        {
                //            var jobConfig = job.CurrrentJob;
                //            var httpClient = new HttpClient();

                //            // 设置Range头以只下载0到1112字节
                //            var request = new HttpRequestMessage(HttpMethod.Get, url);
                //            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, Math.Min(1112, detail.Size ?? 0));

                //            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                //            if (!response.IsSuccessStatusCode)
                //            {
                //                throw new LogicException("无法下载文件");
                //            }

                //            // 直接从 HttpResponseMessage 获取流
                //            var inputStream = await response.Content.ReadAsStreamAsync();

                //            // 解密流
                //            var outputStream = new MemoryStream();

                //            // 解密流
                //            CompressionHelper.DecompressStream(inputStream, outputStream, jobConfig.CompressAlgorithm, jobConfig.EncryptAlgorithm,
                //                jobConfig.EncryptKey, jobConfig.HashAlgorithm, jobConfig.IsEncryptName, out var decryptFileName, true);

                //            outputStream.Seek(0, SeekOrigin.Begin);

                //            if (!string.IsNullOrWhiteSpace(decryptFileName))
                //            {
                //                name = decryptFileName;
                //            }
                //        }

                //        var path = Path.Combine(baseSavePath, name);
                //        DownloadManager.Instance.AddDownloadTask(url, path, jobId, fileId, job.CurrrentDrive.Id, job.AliyunDriveId, job.CurrrentJob.IsEncrypt, job.CurrrentJob.IsEncryptName);
                //    }
                //}

                return Result.Ok();
            }

            return Result.Fail("作业不存在");
        }


        /// <summary>
        /// 暂停下载任务。
        /// </summary>
        /// <param name="taskId">下载任务的唯一标识符。</param>
        [HttpPost("download-pause/{taskId}")]
        public Result PauseDownloadTask(string taskId)
        {
            DownloadManager.Instance.PauseDownloadTask(taskId);
            return Result.Ok();
        }

        /// <summary>
        /// 打开下载目录文件夹，并选中当前下载的文件
        /// </summary>
        /// <param name="taskId"></param>
        /// <returns></returns>
        [HttpPost("download-openfolder/{taskId}")]
        public Result OpenDownloadFolder(string taskId)
        {
            DownloadManager.Instance.OpenDownloadFolder(taskId);
            return Result.Ok();
        }

        /// <summary>
        /// 继续下载任务
        /// </summary>
        /// <param name="taskId"></param>
        /// <returns></returns>
        [HttpPost("download-resume/{taskId}")]
        public Result ResumeDownloadTask(string taskId)
        {
            DownloadManager.Instance.ResumeDownloadTask(taskId);
            return Result.Ok();
        }

        /// <summary>
        /// 移除下载任务。
        /// </summary>
        /// <param name="taskId">下载任务的唯一标识符。</param>
        [HttpDelete("download/{taskId}")]
        public Result RemoveDownloadTask(string taskId)
        {
            DownloadManager.Instance.RemoveDownloadTask(taskId);
            return Result.Ok();
        }

        /// <summary>
        /// 删除已下载的文件。
        /// </summary>
        /// <param name="taskId">下载任务的唯一标识符。</param>
        [HttpDelete("download-removefile/{taskId}")]
        public Result RemoveFile(string taskId)
        {
            DownloadManager.Instance.RemoveFile(taskId);
            return Result.Ok();
        }

        /// <summary>
        /// 获取所有下载任务的状态。
        /// </summary>
        /// <returns>下载任务列表。</returns>
        [HttpGet("download-status")]
        public Result GetDownloadTasks()
        {
            var tasks = DownloadManager.Instance.GetDownloadTasks();
            return Result.Ok(tasks);
        }

        /// <summary>
        /// 获取全局下载速度。
        /// </summary>
        /// <returns>全局下载速度（字节/秒）。</returns>
        [HttpGet("download-globalspeed")]
        public Result GetGlobalDownloadSpeed()
        {
            var speed = DownloadManager.Instance.GetGlobalDownloadSpeed();

            return Result.Ok(new
            {
                Speed = speed,
                SpeedString = speed.ToFileSizeString() + "/s"
            });
        }

        ///// <summary>
        ///// 设置最大并行下载数。
        ///// </summary>
        ///// <param name="maxParallelDownloads">最大并行下载数。</param>
        //[HttpPost("download-maxparallel")]
        //public Result SetMaxParallelDownloads(int maxParallelDownloads)
        //{
        //    DownloadManager.Instance.SetMaxParallelDownloads(maxParallelDownloads);
        //    return Result.Ok();
        //}

        /// <summary>
        /// 获取下载器配置
        /// </summary>
        /// <returns></returns>
        [HttpGet("download-settings")]
        public Result GetSettings()
        {
            return Result.Ok(DownloadManager.Instance.GetSettings());
        }

        /// <summary>
        /// 设置最新并行数和默认下载目录
        /// </summary>
        /// <param name="setting"></param>
        /// <returns></returns>
        [HttpPost("download-settings")]
        public Result SetSettings([FromBody] DownloadManagerSetting setting)
        {
            DownloadManager.Instance.SetSettings(setting);
            return Result.Ok();
        }
    }
}