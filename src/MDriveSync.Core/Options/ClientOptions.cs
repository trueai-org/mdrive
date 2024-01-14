using MDriveSync.Core.DB;

namespace MDriveSync.Core
{
    ///// <summary>
    ///// 客户端备份配置项文件
    ///// </summary>
    //public class ClientSettings
    //{
    //    /// <summary>
    //    /// 客户端配置项
    //    /// </summary>
    //    public const string ClientSettingsPath = "appsettings.Client.json";

    //    /// <summary>
    //    ///
    //    /// </summary>
    //    public ClientOptions Client { get; set; }
    //}

    /// <summary>
    /// 客户端备份配置项
    /// </summary>
    public class ClientOptions
    {
        /// <summary>
        /// 阿里云盘作业配置
        /// </summary>
        public List<AliyunDriveConfig> AliyunDrives { get; set; } = new List<AliyunDriveConfig>();
    }

    /// <summary>
    ///
    /// </summary>
    public class AliyunDriveConfig : IBaseId<string>
    {
        /// <summary>
        /// 云盘唯一标识
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 云盘名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 令牌类型
        /// </summary>
        public string TokenType { get; set; } = "Bearer";

        /// <summary>
        /// 访问令牌
        /// </summary>
        public string AccessToken { get; set; }

        /// <summary>
        /// 刷新令牌
        /// </summary>
        public string RefreshToken { get; set; }

        /// <summary>
        /// 过期时间
        /// </summary>
        public int ExpiresIn { get; set; } = 7200;

        /// <summary>
        /// 挂载点
        /// 空的驱动器盘符
        /// 指定挂载点
        /// </summary>
        public string MountPoint { get; set; }

        /// <summary>
        /// 自动挂载
        /// 启动时挂载磁盘
        /// </summary>
        public bool MountOnStartup { get; set; }

        /// <summary>
        /// 挂载的云盘路径
        /// 可以单独挂在云盘的某个路径，或挂载整个云盘
        /// </summary>
        public string MountPath { get; set; }

        /// <summary>
        /// 是否以只读的方式挂载云盘
        /// </summary>
        public bool MountReadOnly { get; set; }

        /// <summary>
        /// 默认挂载的云盘（资源库、备份盘）
        /// resource | backup
        /// </summary>
        public string MountDrive { get; set; } = "backup";

        /// <summary>
        /// 挂载的云盘是否启用秒传功能
        /// 是否启用秒传功能
        /// 启用阿里云盘秒传
        /// </summary>
        public bool RapidUpload { get; set; } = false;

        /// <summary>
        /// 是否启用回收站，如果启用则删除文件时，保留到回收站
        /// </summary>
        public bool IsRecycleBin { get; set; } = false;

        /// <summary>
        /// 是否已挂载
        /// </summary>
        public bool IsMount { get; set; }

        /// <summary>
        /// 阿里云盘元信息（用户信息、云盘信息、VIP 信息）
        /// </summary>
        public AliyunDriveMetadata Metadata { get; set; }

        /// <summary>
        /// 作业
        /// </summary>
        public List<JobConfig> Jobs { get; set; } = new List<JobConfig>();

        /// <summary>
        /// 保存
        /// </summary>
        public void Save(bool isRemove = false)
        {
            if (isRemove)
            {
                DriveDb.Instacne.Delete(Id);
            }
            else
            {
                var current = DriveDb.Instacne.Get(Id);
                if (current != null)
                {
                    current = this;
                    DriveDb.Instacne.Update(current);
                }
                else
                {
                    DriveDb.Instacne.Add(this);
                }
            }

            //// 读取 JSON 文件
            //var jsonString = string.Empty;
            //if (File.Exists(ClientSettings.ClientSettingsPath))
            //{
            //    jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //}

            //var aliyunDriveConfig = string.IsNullOrWhiteSpace(jsonString) ? null
            //    : JsonSerializer.Deserialize<ClientSettings>(jsonString);
            //aliyunDriveConfig ??= new ClientSettings();
            //aliyunDriveConfig.Client ??= new ClientOptions();

            //var current = aliyunDriveConfig.Client.AliyunDrives.FindIndex(x => x.Id == Id);
            //if (current >= 0)
            //{
            //    aliyunDriveConfig.Client.AliyunDrives.RemoveAt(current);

            //    if (!isRemove)
            //    {
            //        aliyunDriveConfig.Client.AliyunDrives.Insert(current, this);
            //    }
            //}
            //else
            //{
            //    if (!isRemove)
            //    {
            //        aliyunDriveConfig.Client.AliyunDrives.Add(this);
            //    }
            //}

            //// 序列化回 JSON
            //var updatedJsonString = JsonSerializer.Serialize(aliyunDriveConfig, new JsonSerializerOptions
            //{
            //    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            //    WriteIndented = true
            //});

            //// 写入文件
            //File.WriteAllText(ClientSettings.ClientSettingsPath, updatedJsonString, Encoding.UTF8);
        }

        /// <summary>
        /// 保存
        /// </summary>
        public void SaveJob(JobConfig jobConfig, bool isRemove = false)
        {
            if (jobConfig == null)
            {
                return;
            }

            var current = DriveDb.Instacne.Get(Id);
            if (current != null)
            {
                if (isRemove)
                {
                    current.Jobs.RemoveAll(c => c.Id == jobConfig.Id);
                }

                // 保存作业
                var currentJob = Jobs.FirstOrDefault(x => x.Id == jobConfig.Id);
                if (currentJob != null)
                {
                    currentJob = jobConfig;
                }
                else
                {
                    Jobs.Add(jobConfig);
                }

                DriveDb.Instacne.Update(current);
            }

            //// 读取 JSON 文件
            //var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //var aliyunDriveConfig = JsonSerializer.Deserialize<ClientSettings>(jsonString);

            //// 移除重新添加
            //var current = aliyunDriveConfig.Client.AliyunDrives.FindIndex(x => x.Id == Id);
            //if (current >= 0)
            //{
            //    aliyunDriveConfig.Client.AliyunDrives.RemoveAt(current);
            //    aliyunDriveConfig.Client.AliyunDrives.Insert(current, this);
            //}
            //else
            //{
            //    aliyunDriveConfig.Client.AliyunDrives.Add(this);
            //}

            //// 保存作业
            //var currentJob = Jobs.FindIndex(x => x.Id == jobConfig.Id);
            //if (currentJob >= 0)
            //{
            //    Jobs.RemoveAt(currentJob);

            //    if (!isRemove && jobConfig != null)
            //    {
            //        Jobs.Insert(currentJob, jobConfig);
            //    }
            //}
            //else
            //{
            //    if (!isRemove && jobConfig != null)
            //    {
            //        Jobs.Add(jobConfig);
            //    }
            //}

            //// 序列化回 JSON
            //var updatedJsonString = JsonSerializer.Serialize(aliyunDriveConfig, new JsonSerializerOptions
            //{
            //    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            //    WriteIndented = true
            //});

            //// 写入文件
            //File.WriteAllText(ClientSettings.ClientSettingsPath, updatedJsonString, Encoding.UTF8);
        }
    }

    /// <summary>
    /// 阿里云盘元信息（用户信息、云盘信息、VIP 信息）
    /// </summary>
    public class AliyunDriveMetadata
    {
        /// <summary>
        /// 使用容量，单位bytes
        /// </summary>
        public long? UsedSize { get; set; }

        /// <summary>
        /// 总容量，单位bytes
        /// </summary>
        public long? TotalSize { get; set; }

        /// <summary>
        /// 枚举：member, vip, svip
        /// </summary>
        public string Identity { get; set; }

        /// <summary>
        /// 20t、8t
        /// </summary>
        public string Level { get; set; }

        /// <summary>
        /// 过期时间
        /// </summary>
        public DateTime? Expire { get; set; }
    }
}