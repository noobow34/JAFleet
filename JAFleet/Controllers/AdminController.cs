using JAFleet.Models;
using JAFleet.Util;
using Microsoft.AspNetCore.Mvc;

namespace JAFleet.Controllers
{
    /// <summary>管理者向けの機能をまとめた入口。旧 /ql と /Batch の置き換え。</summary>
    public class AdminController : Controller
    {
        public IActionResult Index()
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            return View(new BaseModel { Title = "管理コンソール", IsAdmin = true });
        }
    }
}
