using MDriveSync.Core.Services;
using MDriveSync.Core.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RestSharp;
using System.Net;
using System.Text.Json;

using AliyunDriveProviderOptions = MDriveSync.Core.AliyunDriveProviderOptions;

namespace MDriveSync.Server.API.Controllers
{
    /// <summary>
    /// 后台服务开放接口，用于授权生成签名等
    /// </summary>
    [Route("api/open/aliyundrive")]
    [ApiController]
    public class OpenAliyunDriveController : ControllerBase
    {
        /// <summary>
        /// 阿里云盘服务商配置
        /// </summary>
        private readonly AliyunDriveProviderOptions _providerOptions;

        private readonly IMemoryCache _memoryCache;
        private readonly HttpContext _httpContext;
        private readonly ILogger _logger;

        public OpenAliyunDriveController(
            IMemoryCache memoryCache,
            IHttpContextAccessor contextAccessor,
            ILogger<OpenAliyunDriveController> logger,
            IOptionsMonitor<AliyunDriveProviderOptions> providerOptions)
        {
            _providerOptions = providerOptions.CurrentValue;
            _memoryCache = memoryCache;
            _httpContext = contextAccessor!.HttpContext!;
            _logger = logger;
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
                var client = new RestClient(new RestClientOptions(ProviderApiHelper.ALIYUNDRIVE_API_HOST)
                {
                    MaxTimeout = -1
                });
                var request = new RestRequest("/oauth/access_token", Method.Post);
                request.AddHeader("Content-Type", "application/json");
                object body = new
                {
                    client_id = _providerOptions.AppId,
                    client_secret = _providerOptions.AppSecret,
                    grant_type = "authorization_code",
                    code = code,
                    refresh_token = string.Empty
                };
                request.AddBody(body);

                var response = client.Execute<JsonDocument>(request);

                _logger.LogDebug(response.Content);

                if (response.StatusCode == HttpStatusCode.OK
                    && response.Data != null
                    && response.Data.RootElement.TryGetProperty("refresh_token", out var accessTokenElement))
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

        /// <summary>
        /// 获取阿里云盘登录授权二维码
        /// https://www.yuque.com/aliyundrive/zpfszx/ttfoy0xt2pza8lof
        /// </summary>
        /// <returns></returns>
        [HttpGet("qrcode")]
        public IActionResult GetAuthorizeQrcode([FromQuery] int width = 430, [FromQuery] int height = 430)
        {
            var options = new RestClientOptions(ProviderApiHelper.ALIYUNDRIVE_API_HOST)
            {
                MaxTimeout = -1
            };
            var client = new RestClient(options);
            var request = new RestRequest("/oauth/authorize/qrcode", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            object body = new
            {
                client_id = _providerOptions.AppId,
                client_secret = _providerOptions.AppSecret,
                width = width,
                height = height,
                scopes = new[] { "user:base", "file:all:read", "file:all:write" }
            };
            request.AddBody(body);
            var response = client.ExecuteAsync(request).Result;
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return new ContentResult
                {
                    Content = response.Content,
                    ContentType = "application/json",
                    StatusCode = (int)response.StatusCode
                };

                //using var doc = JsonDocument.Parse(response.Content!);
                //var root = doc.RootElement;
                //var url = root.GetProperty("qrCodeUrl").GetString()!;
                //var sid = root.GetProperty("sid").GetString()!;

                //if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(sid))
                //{
                //    return new JsonResult(new
                //    {
                //        qrCodeUrl = url,
                //        sid = sid
                //    });
                //}
            }

            return new ContentResult
            {
                Content = response?.Content ?? "未知错误",
                ContentType = "application/json",
                StatusCode = (int)(response?.StatusCode ?? HttpStatusCode.InternalServerError)
            };
        }

        /// <summary>
        /// 通过 refresh_token 重新获取令牌
        /// https://www.yuque.com/aliyundrive/zpfszx/efabcs
        /// </summary>
        /// <returns></returns>
        [HttpPost("refresh-token")]
        public IActionResult RefreshToken([FromBody] RefreshTokenRequest param)
        {
            // 重新获取令牌
            var request = new RestRequest("/oauth/access_token", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            object body = new
            {
                client_id = _providerOptions.AppId,
                client_secret = _providerOptions.AppSecret,
                grant_type = "refresh_token",
                code = "",
                refresh_token = param.RefreshToken
            };
            request.AddBody(body);

            // 接口请求
            var options = new RestClientOptions(ProviderApiHelper.ALIYUNDRIVE_API_HOST)
            {
                MaxTimeout = -1
            };
            var apiClient = new RestClient(options);
            var response = apiClient.Execute(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return new ContentResult
                {
                    Content = response.Content,
                    ContentType = "application/json",
                    StatusCode = (int)response.StatusCode
                };
            }

            //var errorContent = response?.Content ?? "未知错误";
            //return new JsonResult(new { error = errorContent })
            //{
            //    StatusCode = (int?)response?.StatusCode ?? 500
            //};

            return new ContentResult
            {
                Content = response?.Content ?? "未知错误",
                ContentType = "application/json",
                StatusCode = (int)(response?.StatusCode ?? HttpStatusCode.InternalServerError)
            };
        }
    }
}