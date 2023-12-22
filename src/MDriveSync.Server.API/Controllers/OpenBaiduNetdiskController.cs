using MDriveSync.Core;
using MDriveSync.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RestSharp;
using System.Net;
using System.Text.Json;

namespace MDriveSync.Server.API.Controllers
{
    /// <summary>
    /// 百度网盘
    /// 后台服务开放接口，用于授权生成签名等
    /// </summary>
    [Route("api/open/baidunetdisk")]
    [ApiController]
    public class OpenBaiduNetdiskController : ControllerBase
    {
        private readonly IMemoryCache _memoryCache;
        private readonly HttpContext _httpContext;
        private readonly ILogger _logger;

        /// <summary>
        /// 百度网盘服务商配置
        /// </summary>
        private readonly BaiduNetDiskProviderOptions _providerOptions;

        public OpenBaiduNetdiskController(
            IMemoryCache memoryCache,
            IHttpContextAccessor contextAccessor,
            ILogger<OpenBaiduNetdiskController> logger,
            IOptionsMonitor<BaiduNetDiskProviderOptions> providerOptions)
        {
            _memoryCache = memoryCache;
            _httpContext = contextAccessor!.HttpContext!;
            _logger = logger;
            _providerOptions = providerOptions.CurrentValue;
        }

        /// <summary>
        /// 通过授权码获取令牌，并保存到内存中
        /// </summary>
        /// <param name="code"></param>
        /// <param name="state">临时标识</param>
        /// <returns></returns>
        [HttpGet]
        public IActionResult Get([FromQuery] string code = "", [FromQuery] string state = "")
        {
            try
            {
                var client = new RestClient(new RestClientOptions(ProviderApiHelper.BAIDU_NETDISK_API_HOST)
                {
                    MaxTimeout = -1
                });
                var request = new RestRequest("/oauth/2.0/token", Method.Get);

                request.AddParameter("grant_type", "authorization_code");
                request.AddParameter("code", code);
                request.AddParameter("client_id", _providerOptions.ClientId);
                request.AddParameter("client_secret", _providerOptions.ClientSecret);
                request.AddParameter("redirect_uri", $"{ProviderApiHelper.DUPLICATI_SERVER_API_HOST}/api/open/baidunetdisk");

                var response = client.Execute<JsonDocument>(request);
                if (response.StatusCode == HttpStatusCode.OK
                    && response.Data != null
                    && response.Data.RootElement.TryGetProperty("access_token", out var accessTokenElement))
                {
                    var token = accessTokenElement.GetString()!;

                    //_logger.LogDebug($"{_httpContext?.Request?.GetIP()}, {code}, {state}, {token}");

                    _memoryCache.Set(state, token, TimeSpan.FromMinutes(10));

                    return Content(token);
                }

                throw response.ErrorException ?? new Exception(response?.Content ?? "授权失败，请重试");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{code}, {state}, code error: {ex.Message}");
            }

            return Ok();
        }

        /// <summary>
        /// 通过临时标识获取内存中的令牌，令牌保持 10 分钟
        /// </summary>
        /// <param name="state">临时标识</param>
        /// <param name="callback"> jsonp </param>
        /// <returns></returns>
        [HttpGet("token")]
        public IActionResult GetToken([FromQuery] string state = "", [FromQuery] string callback = "")
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(state) && _memoryCache.TryGetValue(state, out string token)
                    && !string.IsNullOrWhiteSpace(token))
                {
                    //_logger.LogDebug($"{_httpContext?.Request?.GetIP()}, {callback}, {state}, {token}");

                    if (!string.IsNullOrWhiteSpace(callback))
                    {
                        return Content($"{callback}(\"{token}\")");
                    }

                    return Content(token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{callback}, {state}, token error: {ex.Message}");
            }

            return Ok();
        }
    }
}