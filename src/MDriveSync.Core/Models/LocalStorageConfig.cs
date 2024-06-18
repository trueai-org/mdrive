using MDriveSync.Core.DB;

namespace MDriveSync.Core.Models
{
    /// <summary>
    /// 本地存储作业配置
    /// </summary>
    public class LocalStorageConfig : IBaseId<string>
    {
        /// <summary>
        /// 唯一标识
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 工作组名称（作业分组）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 作业
        /// </summary>
        public List<LocalJobConfig> Jobs { get; set; } = new List<LocalJobConfig>();

        /// <summary>
        /// 保存
        /// </summary>
        public void Save(bool isRemove = false)
        {
            if (isRemove)
            {
                LocalStorageDb.Instance.DB.Delete(Id);
            }
            else
            {
                var current = LocalStorageDb.Instance.DB.Get(Id);
                if (current != null)
                {
                    current = this;
                    LocalStorageDb.Instance.DB.Update(current);
                }
                else
                {
                    LocalStorageDb.Instance.DB.Add(this);
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
        public void SaveJob(LocalJobConfig jobConfig, bool isRemove = false)
        {
            if (jobConfig == null)
            {
                return;
            }

            var current = LocalStorageDb.Instance.DB.Get(Id);
            if (current != null)
            {
                // 保存作业
                var currentJobIndex = Jobs.FindIndex(x => x.Id == jobConfig.Id);
                if (currentJobIndex >= 0)
                {
                    current.Jobs[currentJobIndex] = jobConfig;
                }
                else
                {
                    current.Jobs.Add(jobConfig);
                }

                if (isRemove)
                {
                    current.Jobs.RemoveAll(c => c.Id == jobConfig.Id);
                }

                LocalStorageDb.Instance.DB.Update(current);
            }
        }
    }
}