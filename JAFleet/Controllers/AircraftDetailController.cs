using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using JAFleet.Models;
using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using JAFleet.Commons.Constants;
using Microsoft.EntityFrameworkCore;
using Noobow.Commons.Constants;
using Noobow.Commons.Utils;
using EnumStringValues;

namespace JAFleet.Controllers
{
    public class AircraftDetailController : Controller
    {

        private readonly JAFleetContext _context;
        private readonly IServiceScopeFactory _services;

        public AircraftDetailController(JAFleetContext context, IServiceScopeFactory serviceScopeFactory)
        {
            _context = context;
            _services = serviceScopeFactory;
        }

        public async Task<IActionResult> IndexAsync(string id, [FromQuery] bool nohead, [FromQuery] bool needback, AircraftDetailModel model)
        {

            model.Title = id;
            model.TableId = id;
            model.api = "/api/aircraftwithhistory/" + id;
            model.IsDetail = true;

            model.NoHead = nohead;
            model.IsAdmin = AdminAuth.IsAdmin(HttpContext);
            model.Reg = id;
            model.NeedBack = needback;

            model.AV = _context.AircraftViews.AsNoTracking().Where(av => av.RegistrationNumber == id).FirstOrDefault();
            if (model.AV == null)
            {
                //存在しないレジが指定された場合はNotFound
                return NotFound();
            }
            //OGP の画像は JA-Fleet が描いた機体ごとのカード画像（他人の写真は撮影者の表記・リンクを付けられないので使わない）
            model.OgImageUrl = OgImageController.ImageUrl(model.AV.RegistrationNumber!);
            if (!nohead)
            {
                model.AirlineGroupNmae = _context.AirlineGroups.AsNoTracking().Where(ag => ag.AirlineGroupCode == model.AV.AirlineGroupCode).FirstOrDefault()?.AirlineGroupName;
            }

            //非同期でCookieは取得できなくなるので退避
            bool isAdmin = AdminAuth.IsAdmin(HttpContext);

            string linkUrl = model.AV.LinkUrl ?? string.Empty;
            if (!string.IsNullOrEmpty(model.AV?.PhotoUrl) && !string.IsNullOrEmpty(linkUrl))
            {
                //写真取得できる場合は登録されている写真は削除
                Aircraft a =  _context.Aircrafts.Where(a => a.RegistrationNumber == id).Single();
                a.LinkUrl = null;
                a.ActualUpdateTime = DateTime.Now;
                _context.SaveChanges();
                linkUrl = string.Empty;
                await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), $"{id}のLinkUrlを削除しました");
            }
            if (!string.IsNullOrEmpty(linkUrl))
            {
                //手で登録した写真のページ（JetPhotos に無い機体など）があればそれを出す
                model.PhotoUrl = $"/ADE/{id}";
            }
            else
            {
                //それ以外は Flightradar24 の機体ページをそのまま埋め込む（写真と飛行履歴が見られる）。
                //読み込むのは閲覧者のブラウザなので、こちらのサーバーから FR24 を自動で取りに行くことはない
                model.PhotoUrl = Fr24PageUrl(id);
            }

            //ログは非同期で書き込み
            _ = Task.Run(() =>
            {
                using var serviceScope = _services.CreateScope();
                using JAFleetContext? context = serviceScope.ServiceProvider.GetService<JAFleetContext>();
                Log log = new()
                {
                    LogDate = DateTime.Now,
                    LogType = LogType.DETAIL,
                    LogDetail = id,
                    UserId = isAdmin.ToString(),
                };

                context!.Logs.Add(log);
                context.SaveChanges();
            });

            return View("~/Views/AircraftDetail/index.cshtml", model);
        }

        public IActionResult Emb(string id)
        {
            Aircraft a = _context.Aircrafts.Where(a => a.RegistrationNumber == id).Single();
            if (a.LinkUrl!.StartsWith("<"))
            {
                ViewBag.Tag = a.LinkUrl!;
                return View("~/Views/AircraftDetail/Emb.cshtml");
            }
            else if (a.LinkUrl!.Contains("x.com") || a.LinkUrl!.Contains("twitter.com"))
            {
                //ツイート埋め込みを登録している場合
                ViewBag.TweetUrl = a.LinkUrl!.Replace("x.com","twitter.com");
                return View("~/Views/AircraftDetail/Emb.cshtml");
            }
            else
            {
                //それ意外のサイトを登録している場合
                return Redirect(a.LinkUrl!);
            }
        }

        public Task<IActionResult> IndexNohead(string id, AircraftDetailModel model)
        {
            return IndexAsync(id, true, false, model);
        }

        public Task<IActionResult> IndexNoheadBack(string id, AircraftDetailModel model)
        {
            return IndexAsync(id, true, true, model);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        /// <summary>Flightradar24 の機体ページの URL（例 https://www.flightradar24.com/data/aircraft/ja030a）</summary>
        public static string Fr24PageUrl(string registrationNumber)
            => FlightradarConstant.FR24_DATA_URL + Uri.EscapeDataString(registrationNumber.Trim().ToLowerInvariant());
    }
}
