using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using JAFleet.Services;
using Microsoft.AspNetCore.Mvc;

namespace JAFleet.Controllers
{
    /// <summary>
    /// access_logをまとめて見せる集計画面。1件ずつの記録は/log（logテーブル）が担当で、
    /// こちらはHTTPリクエストそのものを数えている。
    ///
    /// 集計項目が多くDBへの問い合わせが重いため、画面は枠（Index）と中身（Data）に分けてある。
    /// 枠はDBに触らず即座に返し、中身は画面から呼んでもらって後から差し込む。
    /// </summary>
    public class AccessStatsController : Controller
    {
        private readonly JAFleetContext _context;

        public AccessStatsController(JAFleetContext context) => _context = context;

        /// <summary>画面の枠。見出し・期間の切り替え・読み込み中の表示だけを返す</summary>
        public IActionResult Index([FromQuery] int days, [FromQuery] bool includeBot, [FromQuery] bool includeAdmin, [FromQuery] bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            //日数が未指定（0）や想定外の値のときはサービス側が既定の7日に寄せる。
            //ボットと管理者は未指定なら除外（＝falseが既定）
            return View("Index", AccessStatsService.BuildShell(days, includeBot, includeAdmin, fromAdmin));
        }

        /// <summary>集計の中身。画面から呼ばれて部分ビューのHTMLを返す</summary>
        public async Task<IActionResult> DataAsync([FromQuery] int days, [FromQuery] bool includeBot, [FromQuery] bool includeAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            return PartialView("_Stats", await AccessStatsService.BuildAsync(_context, days, includeBot, includeAdmin, false));
        }
    }
}
