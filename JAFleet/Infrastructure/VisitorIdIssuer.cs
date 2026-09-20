using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace JAFleet.Infrastructure
{
    /// <summary>
    /// 訪問者を見分けるためのCookieを発行・検証する。アクセス集計のユニーク数に使う。
    ///
    /// Cookieの中身はDataProtectionで保護した文字列で、暗号化と改ざん検知が付く。
    /// 利用者が値を作ったり書き換えたりしても検証で弾けるので、集計を水増しされない。
    /// 鍵はDataProtectionのキーリングに載り、アプリ名も固定してあるため
    /// （Program.csのSetApplicationName）、デプロイや再起動をまたいでも同じ訪問者として数えられる。
    ///
    /// 個人を特定する情報は入れない。ランダムな16桁とCookieを配った日付だけを持つ。
    /// </summary>
    public class VisitorIdIssuer
    {
        public const string COOKIE_NAME = "jaf_vid";

        //Chromeが受け付けるCookie有効期限の上限が400日
        private const int EXPIRE_DAYS = 400;
        //期限切れで訪問者が入れ替わらないよう、配ってから一定日数たったら同じIDで配り直す。
        //毎回Set-Cookieを返すとキャッシュに乗りにくくなるため、間隔を空けている
        private const int REISSUE_AFTER_DAYS = 30;

        //Cookieを配らないパス。外部から直接叩くAPIや死活監視に配っても意味がない
        private static readonly string[] EXCLUDE_LIST = ["/API", "/CHECK", "/MASTER", "/SETCOOKIE"];

        private readonly IDataProtector _protector;

        public VisitorIdIssuer(IDataProtectionProvider provider)
            => _protector = provider.CreateProtector("JAFleet.VisitorId.v1");

        /// <summary>
        /// リクエストのCookieから訪問者IDを取り出す。無ければ新しいIDを発行してレスポンスに載せる。
        ///
        /// 戻り値は<b>クライアントが送ってきて検証に通ったID</b>だけで、新規発行した分はnullを返す。
        /// 初回の1リクエストが数えられない代わりに、Cookieを持ち回らないクライアント
        /// （多くのボット）がリクエストのたびに別の訪問者として数えられるのを防げる。
        ///
        /// レスポンスの書き込みが始まるとヘッダを足せないため、必ず後続処理の前に呼ぶこと。
        /// </summary>
        public string? ResolveAndIssue(HttpContext context)
        {
            if (!IsTarget(context.Request.Path))
            {
                return null;
            }

            (string? id, DateTime issued) = Read(context);
            if (id == null)
            {
                Write(context, NewId(), DateTime.Now);
                return null;
            }

            if (issued < DateTime.Now.Date.AddDays(-REISSUE_AFTER_DAYS))
            {
                Write(context, id, DateTime.Now);
            }
            return id;
        }

        private static bool IsTarget(PathString path)
            => !EXCLUDE_LIST.Any(s => path.Value!.Contains(s, StringComparison.CurrentCultureIgnoreCase));

        /// <summary>Cookieを読んで(ID, 配った日)を返す。無い・壊れている・改ざんされている場合は(null, 既定値)。</summary>
        private (string?, DateTime) Read(HttpContext context)
        {
            if (!context.Request.Cookies.TryGetValue(COOKIE_NAME, out string? protectedValue) || string.IsNullOrEmpty(protectedValue))
            {
                return (null, default);
            }

            try
            {
                //中身は "ID|発行日(yyyyMMdd)"
                string[] parts = _protector.Unprotect(protectedValue).Split('|');
                if (parts.Length != 2 || parts[0].Length == 0)
                {
                    return (null, default);
                }
                DateTime.TryParseExact(parts[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime issued);
                return (parts[0], issued);
            }
            catch (CryptographicException)
            {
                //改ざん、または鍵が変わって復号できない。作り直す
                return (null, default);
            }
        }

        private void Write(HttpContext context, string id, DateTime issued)
        {
            context.Response.Cookies.Append(COOKIE_NAME, _protector.Protect($"{id}|{issued:yyyyMMdd}"), new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(EXPIRE_DAYS),
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
            });
        }

        private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    }
}
