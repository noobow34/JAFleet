using JAFleet.Commons.EF;
using JAFleet.Infrastructure;
using System.Diagnostics;
using System.Net;

namespace JAFleet.Middleware
{
    public class LoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IServiceScopeFactory _services;

        private static string[] EXCLUDE_LIST = [".CSS", ".JS", ".PNG", ".JPG", ".JPEG", ".GIF", ".ICO", "/CHECK"];

        public LoggingMiddleware(RequestDelegate next, IServiceScopeFactory serviceScopeFactory)
        {
            _next = next;
            _services = serviceScopeFactory;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            bool loggingTarget = !EXCLUDE_LIST.Any(s => httpContext.Request.Path.Value!.Contains(s, StringComparison.CurrentCultureIgnoreCase));
            AccessLog? log = null;
            if (loggingTarget)
            {
                log = new AccessLog
                {
                    RequestTime = DateTime.Now
                    ,
                    RequestIp = httpContext.Request.Headers?["X-Forwarded-For"].FirstOrDefault() != null ?
                                httpContext.Request.Headers["X-Forwarded-For"].First()!.Split(",")[0] : httpContext.Connection.RemoteIpAddress!.MapToIPv4().ToString()
                    ,
                    RequestPath = httpContext.Request.Path
                    ,
                    RequestQuery = httpContext.Request.QueryString.ToString() != string.Empty ? httpContext.Request.QueryString.ToString() : null
                    ,
                    RequestCookies = httpContext.Request.Cookies.Count != 0 ? string.Concat(httpContext.Request.Cookies) : null
                    ,
                    UserAgent = httpContext.Request.Headers?["User-Agent"].FirstOrDefault()
                    ,
                    Referer = httpContext.Request.Headers?["Referer"].FirstOrDefault()
                    ,
                    IsAdmin = CookieUtil.IsAdmin(httpContext)
                };
            }
            Stopwatch? sw = null;
            if (loggingTarget) { sw = new Stopwatch(); sw.Start(); }
            await _next(httpContext);
            if (loggingTarget && log != null)
            {
                sw!.Stop();
                log.ResponseCode = httpContext.Response.StatusCode;
                _ = Task.Run(() =>
                {
                    log.ResponseTime = sw.ElapsedMilliseconds;
                    try
                    {
                        log.RequestHostname = Dns.GetHostEntry(log.RequestIp!).HostName;
                    }
                    catch { }
                    using var serviceScope = _services.CreateScope();
                    using var context = serviceScope.ServiceProvider.GetService<JAFleetContext>();
                    context!.AccessLogs.Add(log);
                    context.SaveChanges();
                });
            }
        }
    }

    public static class LoggingMiddlewareExtensions
    {
        public static IApplicationBuilder UseLoggingMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<LoggingMiddleware>();
        }
    }
}
