using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
#if DEBUG
using System.Security.Claims;
#endif

namespace JAFleet.Infrastructure
{
    /// <summary>
    /// Cloudflare Accessが発行するJWTを検証して管理者を識別する。
    ///
    /// Accessはプロキシとして認証を行い、通過したリクエストにJWTを2通りで渡してくる。
    /// ・Cf-Access-Jwt-Assertionヘッダ … Accessアプリケーションの保護対象パスにのみ付く
    /// ・CF_Authorization Cookie      … ログイン時にホストへ発行され、ブラウザが全パスへ送る
    /// このサイトは大半が認証なしで見えるページで保護対象外のためヘッダは付かないが、
    /// Cookieは届く。これを検証することで公開ページでも管理者を見分けられる。
    /// </summary>
    public static class CloudflareAccess
    {
        public const string SchemeName = "CloudflareAccess";

        /// <summary>Accessアプリケーションで保護しておくログイン用のパス</summary>
        public const string LOGIN_PATH = "/Account/Login";

        /// <summary>Cloudflareが横取りしてCF_Authorizationを破棄するパス</summary>
        public const string LOGOUT_PATH = "/cdn-cgi/access/logout";

        private const string JWT_HEADER = "Cf-Access-Jwt-Assertion";
        private const string JWT_COOKIE = "CF_Authorization";

        /// <summary>
        /// 認証が必要になったときの飛び先。LOGIN_PATHはAccessが保護しているので、
        /// 未認証のブラウザはCloudflareのログイン画面へ誘導される
        /// </summary>
        public static string BuildLoginUrl(string returnUrl)
        {
            string safe = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
            return $"{LOGIN_PATH}?returnUrl={Uri.EscapeDataString(safe)}";
        }

        public static AuthenticationBuilder AddCloudflareAccess(this IServiceCollection services, string teamDomain, string applicationAudience)
        {
            //https://を付けて設定されても動くようにしておく
            string domain = teamDomain.Trim()
                                      .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                                      .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
                                      .TrimEnd('/');
            bool configured = domain.Length != 0 && applicationAudience.Length != 0;
            var keyProvider = new CloudflareAccessKeyProvider(domain);

            return services.AddAuthentication(SchemeName)
                .AddJwtBearer(SchemeName, options =>
                {
                    //AuthorityもMetadataAddressも設定しない。Cloudflare Accessには
                    //OIDCのディスカバリ文書が無く、公開鍵は/cdn-cgi/access/certsにあるため、
                    //IssuerSigningKeyResolverから直接引く
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidIssuer = $"https://{domain}",
                        ValidAudience = applicationAudience,
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKeyResolver = (_, _, kid, _) => keyProvider.Resolve(kid),
                        NameClaimType = "email",
                    };
                    options.MapInboundClaims = false;
                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            //未設定の環境では認証そのものを行わない。全員が一般利用者になる
                            if (configured)
                            {
                                context.Token = context.Request.Headers[JWT_HEADER].FirstOrDefault()
                                                ?? context.Request.Cookies[JWT_COOKIE];
                            }
                            return Task.CompletedTask;
                        },
                        OnChallenge = context =>
                        {
                            //[Authorize]で弾かれたときは401ではなくログインへ送り、Auth0時代と同じ挙動にする
                            context.HandleResponse();
                            if (HttpMethods.IsGet(context.Request.Method))
                            {
                                context.Response.Redirect(BuildLoginUrl(context.Request.Path + context.Request.QueryString));
                            }
                            else
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            }
                            return Task.CompletedTask;
                        },
                    };
                });
        }

#if DEBUG
        /// <summary>
        /// 開発環境で管理画面を確認するために、認証済みの管理者になりすます。
        /// Cloudflareを経由しない手元の実行ではAccessのJWTが手に入らないための逃げ道で、
        /// Development かつ CF_ACCESS_DEV_ADMIN=1 のときだけProgram.csから呼ぶ。
        ///
        /// 全リクエストのUserを差し替えるため、誤って本番で動くと訪問者全員が管理者になる。
        /// 環境変数の設定ミスで発火しないよう、Releaseビルドからは丸ごと除外する
        /// （本番のpublishは -c Release。トンネル経由だと全リクエストがループバック由来に
        /// 見えるため、接続元IPによる制限は防御にならない）
        /// </summary>
        public static IApplicationBuilder UseCloudflareAccessDevAdmin(this IApplicationBuilder builder)
        {
            Console.WriteLine("[CloudflareAccess] 開発用の管理者なりすましが有効です。本番では絶対に有効にしないこと");
            return builder.Use(async (context, next) =>
            {
                var identity = new ClaimsIdentity([new Claim("email", "dev@localhost")], "CloudflareAccessDevAdmin", "email", ClaimTypes.Role);
                context.User = new ClaimsPrincipal(identity);
                await next();
            });
        }
#endif
    }

    /// <summary>
    /// /cdn-cgi/access/certs から署名検証用の公開鍵を取得してキャッシュする。
    /// </summary>
    internal sealed class CloudflareAccessKeyProvider
    {
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromHours(1);
        //未知のkidを持つトークンを投げ続けられても取得しに行かないよう、再取得の下限を設ける
        private static readonly TimeSpan MIN_REFRESH_INTERVAL = TimeSpan.FromMinutes(5);

        private readonly string _certsUrl;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private List<SecurityKey> _keys = [];
        private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

        public CloudflareAccessKeyProvider(string teamDomain)
            => _certsUrl = $"https://{teamDomain}/cdn-cgi/access/certs";

        public IEnumerable<SecurityKey> Resolve(string? kid)
        {
            List<SecurityKey> keys = GetKeys(forceRefresh: false);
            //鍵のローテーション直後は手元に無いkidが来る。そのときだけ取り直す
            if (!string.IsNullOrEmpty(kid) && !keys.Any(k => k.KeyId == kid))
            {
                keys = GetKeys(forceRefresh: true);
            }
            return keys;
        }

        private List<SecurityKey> GetKeys(bool forceRefresh)
        {
            if (!forceRefresh && _keys.Count != 0 && DateTimeOffset.UtcNow - _fetchedAt < CACHE_DURATION)
            {
                return _keys;
            }
            if (forceRefresh && DateTimeOffset.UtcNow - _fetchedAt < MIN_REFRESH_INTERVAL)
            {
                return _keys;
            }

            _lock.Wait();
            try
            {
                //ロック待ちの間に他のリクエストが取得済みなら、それを使う
                if (DateTimeOffset.UtcNow - _fetchedAt < MIN_REFRESH_INTERVAL)
                {
                    return _keys;
                }

                string json = HttpClientManager.GetInstance().GetStringAsync(_certsUrl).GetAwaiter().GetResult();
                _keys = [.. JsonWebKeySet.Create(json).GetSigningKeys()];
            }
            catch (Exception ex)
            {
                //取得に失敗しても公開ページは動かし続ける。手持ちの鍵で検証を続け、
                //鍵が無ければ検証に失敗して未認証扱いになるだけ
                Console.WriteLine($"[CloudflareAccess] 公開鍵の取得に失敗しました: {ex.Message}");
            }
            finally
            {
                //成功・失敗にかかわらず記録し、失敗時に取得を繰り返さないようにする
                _fetchedAt = DateTimeOffset.UtcNow;
                _lock.Release();
            }
            return _keys;
        }
    }
}
