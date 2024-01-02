using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MDriveSync.Core
{
    /// <summary>
    /// 客户端备份配置项文件
    /// </summary>
    public class ClientSettings
    {
        /// <summary>
        /// 客户端配置项
        /// </summary>
        public const string ClientSettingsPath = "appsettings.Client.json";

        /// <summary>
        ///
        /// </summary>
        public ClientOptions Client { get; set; }
    }

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
    public class AliyunDriveConfig
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
        public void Save()
        {
            // 读取 JSON 文件
            var jsonString = string.Empty;
            if (File.Exists(ClientSettings.ClientSettingsPath))
            {
                jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            }

            var aliyunDriveConfig = string.IsNullOrWhiteSpace(jsonString) ? null
                : JsonSerializer.Deserialize<ClientSettings>(jsonString);
            aliyunDriveConfig ??= new ClientSettings();
            aliyunDriveConfig.Client ??= new ClientOptions();

            // 移除重新添加
            var current = aliyunDriveConfig.Client.AliyunDrives.FindIndex(x => x.Id == Id);
            if (current >= 0)
            {
                aliyunDriveConfig.Client.AliyunDrives.RemoveAt(current);
                aliyunDriveConfig.Client.AliyunDrives.Insert(current, this);
            }
            else
            {
                aliyunDriveConfig.Client.AliyunDrives.Add(this);
            }

            // 序列化回 JSON
            var updatedJsonString = JsonSerializer.Serialize(aliyunDriveConfig, new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            });

            // 写入文件
            File.WriteAllText(ClientSettings.ClientSettingsPath, updatedJsonString, Encoding.UTF8);
        }

        /// <summary>
        /// 保存
        /// </summary>
        public void SaveJob(JobConfig jobConfig)
        {
            // 读取 JSON 文件
            var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            var aliyunDriveConfig = JsonSerializer.Deserialize<ClientSettings>(jsonString);

            // 移除重新添加
            var current = aliyunDriveConfig.Client.AliyunDrives.FindIndex(x => x.Id == Id);
            if (current >= 0)
            {
                aliyunDriveConfig.Client.AliyunDrives.RemoveAt(current);
                aliyunDriveConfig.Client.AliyunDrives.Insert(current, this);
            }
            else
            {
                aliyunDriveConfig.Client.AliyunDrives.Add(this);
            }

            // 保存作业
            var currentJob = Jobs.FindIndex(x => x.Id == jobConfig.Id);
            if (currentJob >= 0)
            {
                Jobs.RemoveAt(currentJob);
                Jobs.Insert(currentJob, jobConfig);
            }
            else
            {
                Jobs.Add(jobConfig);
            }

            // 序列化回 JSON
            var updatedJsonString = JsonSerializer.Serialize(aliyunDriveConfig, new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            });

            // 写入文件
            File.WriteAllText(ClientSettings.ClientSettingsPath, updatedJsonString, Encoding.UTF8);
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