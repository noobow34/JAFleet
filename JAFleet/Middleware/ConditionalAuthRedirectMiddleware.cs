using JAFleet.Infrastructure;

namespace JAFleet.Middleware
{
    /// <summary>
    /// 管理者の端末だけを自動でログインへ誘導する。
    /// サイトの大半は認証なしで見えるため、Cloudflare Accessの保護対象は
    /// /Account/Login などに絞ってある。ここでそこへリダイレクトすることで、
    /// 目印のCookieを持つ端末にだけAccessのログイン画面を出す。
    /// </summary>
    public class ConditionalAuthRedirectMiddleware
    {
        private readonly RequestDelegate _next;
        //"/LOG"を除外していた頃は/logの認可を[Authorize]が担っていたが、AdminAuth.IsAdminへ
        //統一したのでここで自動ログインを効かせる。"/LOG"が/Account/Logoutも巻き込んで
        //除外していたため、そちらは明示的に並べておく
        private static string[] EXCLUDE_LIST = [".CSS", ".JS", ".PNG", ".JPG", ".JPEG", ".GIF", ".ICO", "/CHECK", "/ACCOUNT/LOGIN", "/ACCOUNT/LOGOUT", "/SETCOOKIE", "/API", "/MASTER"];
        private static readonly string adminKey = Environment.GetEnvironmentVariable("ADMIN_KEY") ?? "";
        private static readonly string adminValue = Environment.GetEnvironmentVariable("ADMIN_VALUE") ?? "";

        public ConditionalAuthRedirectMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            bool autoLoginTarget = !EXCLUDE_LIST.Any(s => context.Request.Path.Value!.Contains(s, StringComparison.CurrentCultureIgnoreCase));
            if (context.User.Identity!.IsAuthenticated || !autoLoginTarget)
            {
                await _next(context);
                return;
            }

            context.Request.Cookies.TryGetValue(adminKey, out string? adminCookieValue);
            if (adminKey.Length != 0 && adminCookieValue == adminValue)
            {
                string returnUrl = context.Request.Path + context.Request.QueryString;
                IssueMarker(context);
                context.Response.Redirect(CloudflareAccess.BuildLoginUrl(returnUrl));
                return;
            }

            await _next(context);
        }

        /// <summary>
        /// 目印のCookieを発行（または期限を延長）する。
        /// /Account/Loginを通過した端末にも配るので、新しい端末でも一度ログインすれば
        /// 次から自動ログインの対象になる。値はサーバの環境変数から出すだけで、端末間で持ち運ぶ必要はない。
        /// 目印は「Accessのログインへ案内する」合図にすぎず、漏れても管理者にはなれない
        /// </summary>
        public static void IssueMarker(HttpContext context)
        {
            if (adminKey.Length == 0 || adminValue.Length == 0)
            {
                return;
            }

            context.Response.Cookies.Append(adminKey, adminValue, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                Secure = true,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
            });
        }
    }
}
