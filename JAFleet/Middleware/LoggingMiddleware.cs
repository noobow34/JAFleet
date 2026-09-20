using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using System.Diagnostics;
using System.Net;

namespace JAFleet.Middleware
{
    public class LoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IServiceScopeFactory _services;
        private readonly VisitorIdIssuer _visitorIdIssuer;

        private static string[] EXCLUDE_LIST = [".CSS", ".JS", ".PNG", ".JPG", ".JPEG", ".GIF", ".ICO", "/CHECK"];

        public LoggingMiddleware(RequestDelegate next, IServiceScopeFactory serviceScopeFactory, VisitorIdIssuer visitorIdIssuer)
        {
            _next = next;
            _services = serviceScopeFactory;
            _visitorIdIssuer = visitorIdIssuer;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            bool loggingTarget = !EXCLUDE_LIST.Any(s => httpContext.Request.Path.Value!.Contains(s, StringComparison.CurrentCultureIgnoreCase));
            AccessLog? log = null;
            if (loggingTarget)
            {
                //Cookieの発行はレスポンスの書き込みが始まる前にしか出来ないため、後続処理へ渡す前に済ませる
                string? visitorId = _visitorIdIssuer.ResolveAndIssue(httpContext);
                //訪問者識別のCookieは中身が保護済みの長い文字列で、見るべき値はvisitor_idに入っている。
                //全リクエスト分をそのまま持つとaccess_logが太るだけなのでここでは外す
                var cookies = httpContext.Request.Cookies.Where(c => c.Key != VisitorIdIssuer.COOKIE_NAME).ToList();

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
                    RequestCookies = cookies.Count != 0 ? string.Concat(cookies) : null
                    ,
                    UserAgent = httpContext.Request.Headers?["User-Agent"].FirstOrDefault()
                    ,
                    Referer = httpContext.Request.Headers?["Referer"].FirstOrDefault()
                    ,
                    VisitorId = visitorId
                };
            }
            Stopwatch? sw = null;
            if (loggingTarget) { sw = new Stopwatch(); sw.Start(); }
            await _next(httpContext);
            if (loggingTarget && log != null)
            {
                sw!.Stop();
                log.ResponseCode = httpContext.Response.StatusCode;
                //このミドルウェアはUseAuthenticationより前に入るため、後続処理を通したあとでないと
                //User.Identityが埋まっていない。ここで見ないとis_adminが常にfalseになる
                log.IsAdmin = AdminAuth.IsAdmin(httpContext);
                //ルーティングも後続処理の中で行われる。静的ファイルやどのルートにも当たらなかった
                //リクエストではコントローラー名が取れず、アクセス集計はそれを「ページではない」と見る
                log.RouteKey = RouteKey(httpContext);
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

        /// <summary>"コントローラー名/アクション名"。ルートに当たらなかったリクエストはnull</summary>
        private static string? RouteKey(HttpContext httpContext)
        {
            RouteValueDictionary values = httpContext.GetRouteData().Values;
            if (values["controller"] is not string controller || controller.Length == 0)
            {
                return null;
            }
            return values["action"] is string action && action.Length != 0 ? $"{controller}/{action}" : controller;
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
