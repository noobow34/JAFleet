using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using JAFleet.Models;
using JAFleet.Commons.EF;
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
            model.IsAdmin = CookieUtil.IsAdmin(HttpContext);
            model.Reg = id;
            model.NeedBack = needback;

            model.AV = _context.AircraftViews.AsNoTracking().Where(av => av.RegistrationNumber == id).FirstOrDefault();
            model.OgImageUrl = model.AV?.PhotoDirectLarge ?? "https://ja-fleet.noobow.me/images/JA-Fleet_1_og.png";
            if (model.AV == null)
            {
                //存在しないレジが指定された場合はNotFound
                return NotFound();
            }
            if (!nohead)
            {
                model.AirlineGroupNmae = _context.AirlineGroups.AsNoTracking().Where(ag => ag.AirlineGroupCode == model.AV.AirlineGroupCode).FirstOrDefault()?.AirlineGroupName;
            }

            //非同期でCookieは取得できなくなるので退避
            bool isAdmin = CookieUtil.IsAdmin(HttpContext);

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
                model.PhotoUrl = $"/ADE/{id}";
            }
            else
            {
                model.PhotoUrl = model.AV?.PhotoUrl ?? "/nophoto.html";
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
    }
}
