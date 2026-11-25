using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using JAFleet.Models;
using JAFleet.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Controllers
{
    /// <summary>
    /// 管理者の棚卸（/Inventory）。検索画面と同じ条件で機体を探し、内容を確かめた機体に目印（inventory_check）を付ける。
    /// 目印のあとで機体が更新されていたら「要再確認」として返す（目印は消さない）。
    /// 検索の記録（search_condition・ログ）は残さない。
    /// </summary>
    public class InventoryController : Controller
    {
        private readonly JAFleetContext _context;

        public InventoryController(JAFleetContext context) => _context = context;

        public IActionResult Index([FromQuery] bool fromAdmin, [FromQuery] string? sc)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            var model = new SearchModel();
            if (!string.IsNullOrEmpty(sc))
            {
                //保存済みの検索条件（検索画面の ?sc=）から開いたときは、その条件を画面で復元して検索する
                ViewData["InitialCondition"] = _context.SearchConditions.AsNoTracking()
                    .Where(e => e.SearchConditionKey == sc).Select(e => e.SearchConditionJson).FirstOrDefault();
            }

            model.Title = "棚卸";
            model.IsAdmin = true;
            model.FromAdmin = fromAdmin;
            model.AirlineList = MasterManager.AllAirline!;
            model.TypeDetailList = MasterManager.TypeDetailGroup!;
            model.OperationList = MasterManager.Operation!;
            model.WiFiList = MasterManager.Wifi!;
            return View(model);
        }

        /// <summary>検索画面と同じ条件で探し、全項目と目印を返す</summary>
        [HttpPost]
        public IActionResult Search(SearchModel model)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            AircraftView[] result;
            try
            {
                result = AircraftSearch.Build(_context, model, out List<string> historyLiveryRegs).ToArray();
                AircraftSearch.MarkHistoryLivery(result, historyLiveryRegs);
            }
            catch (Npgsql.PostgresException ex) when (AircraftSearch.IsInvalidRegex(ex))
            {
                return Json(new { errorMessage = AircraftSearch.INVALID_REGEX_MESSAGE });
            }

            //目印は件数が少ないので全部読んで突き合わせる（レジの IN 検索にすると、全機体のときに長い SQL になる）
            Dictionary<string, DateTime> checks = _context.InventoryChecks.AsNoTracking()
                .ToDictionary(c => c.RegistrationNumber, c => c.CheckedAt);

            var rows = result.Select(a =>
            {
                DateTime? checkedAt = checks.TryGetValue(a.RegistrationNumber, out DateTime t) ? t : null;
                return new
                {
                    reg = a.RegistrationNumber,
                    airline = a.AirlineNameJpShort,
                    type = a.TypeDetailName,
                    serial = a.SerialNumber,
                    registerDate = a.RegisterDate?.ToString("yyyy/MM/dd"),
                    deregisterDate = a.DeregisterDate?.ToString("yyyy/MM/dd"),
                    operationCode = a.OperationCode,
                    operation = a.Operation,
                    wifi = a.WifiShort,
                    seatConfig = a.SeatConfig,
                    specialLivery = a.SpecialLivery,
                    remarks = a.Remarks,
                    testReg = a.TestRegistration,
                    linkUrl = a.LinkUrl,
                    updated = a.ActualUpdateTime?.ToString("yyyy/MM/dd HH:mm"),
                    checkedAt = checkedAt?.ToString("yyyy/MM/dd HH:mm"),
                    stale = IsStale(checkedAt, a.ActualUpdateTime),
                };
            }).ToArray();

            return Json(new { list = rows });
        }

        /// <summary>
        /// 目印を付ける（on=true）・外す（on=false）。regs は複数渡せる（表示中の機体をまとめて付けるとき）。
        /// 付けたときは、要再確認でなくなった時刻を返す
        /// </summary>
        [HttpPost]
        public IActionResult Check(List<string> regs, bool on)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }
            regs = regs.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
            if (regs.Count == 0)
            {
                return BadRequest();
            }

            var existing = _context.InventoryChecks.Where(c => regs.Contains(c.RegistrationNumber)).ToDictionary(c => c.RegistrationNumber);
            DateTime now = DateTime.Now;
            if (on)
            {
                foreach (string reg in regs)
                {
                    if (existing.TryGetValue(reg, out InventoryCheck? c))
                    {
                        c.CheckedAt = now;
                    }
                    else
                    {
                        _context.InventoryChecks.Add(new InventoryCheck { RegistrationNumber = reg, CheckedAt = now });
                    }
                }
            }
            else
            {
                _context.InventoryChecks.RemoveRange(existing.Values);
            }
            _context.SaveChanges();

            return Json(new { checkedAt = on ? now.ToString("yyyy/MM/dd HH:mm") : null, count = regs.Count });
        }

        /// <summary>目印のあとで機体が更新されたか</summary>
        internal static bool IsStale(DateTime? checkedAt, DateTime? updated) =>
            checkedAt != null && updated != null && updated > checkedAt;
    }
}
