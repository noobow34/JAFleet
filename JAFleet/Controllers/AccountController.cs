using JAFleet.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace JAFleet.Controllers
{
    public class AccountController : Controller
    {
        /// <summary>
        /// Cloudflare Accessの保護対象パス。
        /// ここへ到達した時点でAccessの認証は済んでおり、CF_Authorizationも発行済みなので、
        /// アプリ側ですることは元のページへ戻すことだけ。
        /// </summary>
        public IActionResult Login(string returnUrl = "/")
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                //Accessがこのパスを保護していないか、JWTの検証に失敗している。
                //ここでreturnUrlへ戻すと自動ログインのリダイレクトと往復し続けるので止める
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Cloudflare Accessの認証情報を確認できませんでした。/Account/LoginがAccessアプリケーションの対象に含まれているか、CF_ACCESS_TEAM_DOMAINとCF_ACCESS_AUDが正しいか確認してください。");
            }

            return Redirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
        }

        public IActionResult Logout()
        {
            //自動ログインの目印を消しておかないと、ログアウト直後にまた/Account/Loginへ
            //飛ばされてログアウトにならない
            string adminKey = Environment.GetEnvironmentVariable("ADMIN_KEY") ?? "";
            if (adminKey.Length != 0)
            {
                Response.Cookies.Delete(adminKey);
            }

            //Cloudflareがこのパスを横取りしてCF_Authorizationを破棄する
            return Redirect(CloudflareAccess.LOGOUT_PATH);
        }
    }
}
