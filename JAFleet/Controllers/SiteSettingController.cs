using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using JAFleet.Models;
using JAFleet.Services;
using Microsoft.AspNetCore.Mvc;

namespace JAFleet.Controllers
{
    /// <summary>
    /// サイト設定（site_setting）の画面。保存するとメモリの値も差し替えるので、再起動なしで反映される。
    /// </summary>
    public class SiteSettingController : Controller
    {
        private const string TITLE = "サイト設定";

        private readonly JAFleetContext _context;

        public SiteSettingController(JAFleetContext context) => _context = context;

        public IActionResult Index([FromQuery] bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            return View(BuildModel(fromAdmin));
        }

        [HttpPost]
        public IActionResult Save(bool showNotWorkingInfoMenu, string? workingStatusSource, bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            var source = string.Equals(workingStatusSource, "fr24", StringComparison.OrdinalIgnoreCase)
                ? WorkingStatusSource.Fr24
                : WorkingStatusSource.Adsb;
            try
            {
                SiteSettings.SaveBool(_context, SiteSettings.SHOW_NOT_WORKING_INFO_MENU, showNotWorkingInfoMenu);
                SiteSettings.SaveSource(_context, source);
            }
            catch (Exception ex)
            {
                SiteSettingModel ng = BuildModel(fromAdmin);
                //保存できなかった内容を画面に残す
                ng.ShowNotWorkingInfoMenu = showNotWorkingInfoMenu;
                ng.WorkingStatusSource = source;
                ng.ErrorMessage = "保存できませんでした。site_setting テーブルが無い場合は "
                                + $"Ddl/create_site_setting.sql を実行してください。（{ex.GetBaseException().Message}）";
                return View("Index", ng);
            }

            SiteSettingModel model = BuildModel(fromAdmin);
            model.Message = (showNotWorkingInfoMenu
                ? "メニューに「非稼働情報」を表示するようにしました。"
                : "メニューから「非稼働情報」を外しました。ページ自体は管理コンソールから開けます。")
                + (source == WorkingStatusSource.Adsb
                    ? "稼働状況は adsb.lol の履歴ダンプ（RefreshAdsbFlightJob）から作ります。"
                    : "稼働状況は Flightradar24 の巡回（RefreshWorkingStatusAndPhotoJob・NotifyWorkingStatusJob）で作ります。");
            return View("Index", model);
        }

        private static SiteSettingModel BuildModel(bool fromAdmin) => new()
        {
            Title = TITLE,
            IsAdmin = true,
            FromAdmin = fromAdmin,
            ShowNotWorkingInfoMenu = SiteSettings.ShowNotWorkingInfoMenu,
            WorkingStatusSource = SiteSettings.WorkingStatusSource,
        };
    }
}
