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
        public object Metadata { get; set; }

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
            var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            var aliyunDriveConfig = JsonSerializer.Deserialize<ClientSettings>(jsonString);

            // 移除重新添加
            var current = aliyunDriveConfig.Client.AliyunDrives.FindIndex(x => x.Name == Name);
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
    }
}