using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using JAFleet.Models;
using JAFleet.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Controllers
{
    /// <summary>
    /// 管理者の棚卸（/Inventory）。検索画面と同じ条件で機体を探し、内容を確かめた機体に目印を付ける。
    /// 棚卸は名前を付けて複数保存でき（inventory_session）、目印（inventory_check）は棚卸ごとに持つ。
    /// 目印のあとで機体が更新されていたら「要再確認」として返す（目印は消さない）。
    /// 条件に当たる機体が全部確認済になったら、その棚卸は自動で完了になる。
    /// 検索の記録（search_condition・ログ）は残さない。
    /// </summary>
    public class InventoryController : Controller
    {
        private readonly JAFleetContext _context;

        public InventoryController(JAFleetContext context) => _context = context;

        /// <param name="sc">検索画面の保存済み条件のキー。その条件を欄に入れて検索する（まだ棚卸にはしない）</param>
        /// <param name="session">開く棚卸。どちらも無ければ、最後に開いた未完了の棚卸を開く</param>
        public IActionResult Index([FromQuery] bool fromAdmin, [FromQuery] string? sc, [FromQuery] int? session)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            if (!string.IsNullOrEmpty(sc))
            {
                ViewData["InitialCondition"] = _context.SearchConditions.AsNoTracking()
                    .Where(e => e.SearchConditionKey == sc).Select(e => e.SearchConditionJson).FirstOrDefault();
            }
            else
            {
                ViewData["InitialSession"] = session ?? _context.InventorySessions.AsNoTracking()
                    .Where(s => s.CompletedAt == null)
                    .OrderByDescending(s => s.LastOpenedAt)
                    .Select(s => (int?)s.Id)
                    .FirstOrDefault();
            }

            var model = new SearchModel
            {
                Title = "棚卸",
                IsAdmin = true,
                FromAdmin = fromAdmin,
                AirlineList = MasterManager.AllAirline!,
                TypeDetailList = MasterManager.TypeDetailGroup!,
                OperationList = MasterManager.Operation!,
                WiFiList = MasterManager.Wifi!,
            };
            return View(model);
        }

        /// <summary>保存した棚卸の一覧（未完了を最後に開いた順、そのあと完了を完了の新しい順）</summary>
        public IActionResult Sessions()
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }
            return Json(new { sessions = LoadSessions() });
        }

        /// <summary>
        /// 機体を探し、全項目と目印を返す。sessionId を渡すとその棚卸の条件で探し（送った条件は使わない）、
        /// 進み具合を数え直して、全部確認済なら完了にする。渡さなければ送った条件で探すだけ（目印は無し）
        /// </summary>
        [HttpPost]
        public IActionResult Search(SearchModel model, int? sessionId)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            InventorySession? session = null;
            if (sessionId != null)
            {
                session = _context.InventorySessions.Find(sessionId.Value);
                if (session == null)
                {
                    return Json(new { errorMessage = "この棚卸は削除されています。" });
                }
                model = InventoryStore.ToSearchModel(session.ConditionJson);
                session.LastOpenedAt = DateTime.Now;
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

            Dictionary<string, DateTime> checks = session == null ? [] : LoadChecks(session.Id);
            if (session != null)
            {
                InventoryStore.UpdateProgress(session, result, checks, DateTime.Now);
                _context.SaveChanges();
            }

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
                    stale = InventoryStore.IsStale(checkedAt, a.ActualUpdateTime),
                };
            }).ToArray();

            return Json(new { list = rows, session = session == null ? null : ToJson(session) });
        }

        /// <summary>送った条件で棚卸を始める（名前を付けて保存する）</summary>
        [HttpPost]
        public IActionResult Create(SearchConditionInModel condition, string name)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 100)
            {
                return BadRequest("名前は 1〜100 文字で付けてください。");
            }
            DateTime now = DateTime.Now;
            var session = new InventorySession
            {
                Name = name,
                ConditionJson = condition.ToString(),
                CreatedAt = now,
                LastOpenedAt = now,
            };
            _context.InventorySessions.Add(session);
            _context.SaveChanges();
            return Json(new { session = ToJson(session) });
        }

        /// <summary>棚卸の条件を変える（目印は残す。新しく条件に入った機体は未確認になる）</summary>
        [HttpPost]
        public IActionResult UpdateCondition(int sessionId, SearchConditionInModel condition)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }
            InventorySession? session = _context.InventorySessions.Find(sessionId);
            if (session == null)
            {
                return NotFound();
            }
            session.ConditionJson = condition.ToString();
            _context.SaveChanges();
            return Json(new { session = ToJson(session) });
        }

        /// <summary>手で完了にする（completed=true）・未完了に戻す（false）</summary>
        [HttpPost]
        public IActionResult SetCompleted(int sessionId, bool completed)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }
            InventorySession? session = _context.InventorySessions.Find(sessionId);
            if (session == null)
            {
                return NotFound();
            }
            session.CompletedAt = completed ? DateTime.Now : null;
            session.CompletedManually = completed;
            _context.SaveChanges();
            return Json(new { session = ToJson(session) });
        }

        /// <summary>棚卸を消す。目印を先に消してから棚卸を消す（外部キーがあるため）</summary>
        [HttpPost]
        public async Task<IActionResult> Delete(int sessionId)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }
            using var tx = await _context.Database.BeginTransactionAsync();
            await _context.InventoryChecks.Where(c => c.SessionId == sessionId).ExecuteDeleteAsync();
            int deleted = await _context.InventorySessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
            await tx.CommitAsync();
            return Json(new { deleted });
        }

        /// <summary>
        /// 目印を付ける（on=true）・外す（on=false）。regs は複数渡せる（表示中の機体をまとめて付けるとき）。
        /// 付け外しのあと棚卸の進み具合を数え直し、全部確認済になったら完了にする
        /// </summary>
        [HttpPost]
        public IActionResult Check(int sessionId, List<string> regs, bool on)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }
            regs = regs.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
            InventorySession? session = _context.InventorySessions.Find(sessionId);
            if (regs.Count == 0 || session == null)
            {
                return BadRequest();
            }

            var existing = _context.InventoryChecks
                .Where(c => c.SessionId == sessionId && regs.Contains(c.RegistrationNumber))
                .ToDictionary(c => c.RegistrationNumber);
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
                        _context.InventoryChecks.Add(new InventoryCheck { SessionId = sessionId, RegistrationNumber = reg, CheckedAt = now });
                    }
                }
            }
            else
            {
                _context.InventoryChecks.RemoveRange(existing.Values);
            }
            _context.SaveChanges();

            //進み具合は条件に当たる機体で数え直す（ほかの画面で機体が増えた・更新されたぶんも入る）
            AircraftView[] result = AircraftSearch.Build(_context, InventoryStore.ToSearchModel(session.ConditionJson), out _).ToArray();
            InventoryStore.UpdateProgress(session, result, LoadChecks(sessionId), now);
            _context.SaveChanges();

            return Json(new { checkedAt = on ? now.ToString("yyyy/MM/dd HH:mm") : null, session = ToJson(session) });
        }

        private Dictionary<string, DateTime> LoadChecks(int sessionId) =>
            _context.InventoryChecks.AsNoTracking()
                .Where(c => c.SessionId == sessionId)
                .ToDictionary(c => c.RegistrationNumber, c => c.CheckedAt);

        private object[] LoadSessions()
        {
            List<InventorySession> all = _context.InventorySessions.AsNoTracking().ToList();
            return all.Where(s => s.CompletedAt == null).OrderByDescending(s => s.LastOpenedAt)
                .Concat(all.Where(s => s.CompletedAt != null).OrderByDescending(s => s.CompletedAt))
                .Select(ToJson)
                .ToArray();
        }

        private static object ToJson(InventorySession s) => new
        {
            id = s.Id,
            name = s.Name,
            condition = s.ConditionJson,
            total = s.TotalCount,
            checkedCount = s.CheckedCount,
            createdAt = s.CreatedAt.ToString("yyyy/MM/dd HH:mm"),
            completedAt = s.CompletedAt?.ToString("yyyy/MM/dd HH:mm"),
            completedManually = s.CompletedManually,
        };
    }
}
