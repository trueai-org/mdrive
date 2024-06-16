using MDriveSync.Core.DB;
using Microsoft.Extensions.Caching.Memory;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 阿里云 Drive Token 管理
    /// </summary>
    public class AliyunDriveToken : SingletonBase<AliyunDriveToken>
    {
        /// <summary>
        /// 锁
        /// </summary>
        private static readonly object _lock = new();

        /// <summary>
        /// 本地缓存
        /// 令牌缓存、下载链接缓存等
        /// </summary>
        private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

        /// <summary>
        /// 获取指定 Drive 的访问令牌
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public string GetAccessToken(string id)
        {
            lock (_lock)
            {
                return _cache.GetOrCreate($"TOKEN:{id}", c =>
                {
                    var config = AliyunStorageDb.Instance.DB.Get(id);
                    if (config == null)
                    {
                        throw new Exception("未找到指定的 Drive 配置");
                    }

                    // 重新获取令牌
                    var data = ProviderApiHelper.RefreshToken(config.RefreshToken);
                    if (data == null)
                    {
                        throw new Exception("刷新访问令牌失败");
                    }

                    config.TokenType = data.TokenType;
                    config.AccessToken = data.AccessToken;
                    config.RefreshToken = data.RefreshToken;
                    config.ExpiresIn = data.ExpiresIn;
                    config.Save();

                    // 提前 5 分钟过期
                    c.SetAbsoluteExpiration(TimeSpan.FromSeconds(config.ExpiresIn - 60 * 5));

                    return data.AccessToken;
                });
            }
        }
    }
}