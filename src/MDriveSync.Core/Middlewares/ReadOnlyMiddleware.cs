using Microsoft.AspNetCore.Http;

namespace MDriveSync.Core.Middlewares
{
    /// <summary>
    /// 只读模式中间件
    /// 只读模式下，只允许 GET 请求
    /// </summary>
    public class ReadOnlyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly bool? _isReadOnlyMode;

        public ReadOnlyMiddleware(RequestDelegate next, bool? isReadOnlyMode)
        {
            _next = next;
            _isReadOnlyMode = isReadOnlyMode;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (_isReadOnlyMode == true && context.Request.Method != HttpMethods.Get)
            {
                // 如果是只读模式且请求不是 GET 请求，则返回错误
                context.Response.StatusCode = StatusCodes.Status200OK;

                await context.Response.WriteAsJsonAsync(Result.Fail("只读模式下不允许此操作"));
            }
            else
            {
                // 否则继续处理请求
                await _next(context);
            }
        }
    }
}