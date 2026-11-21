using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using JAFleet.Jobs;
using JAFleet.Models;
using JAFleet.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Controllers
{
    /// <summary>
    /// 機体更新の予約（aircraft_reservation）の一覧。予約は単票編集の「予約日」と特別塗装ニュースの承認から作る。
    /// ここでは確認と取消、反映日が来た分をジョブを待たずに反映することだけができる。
    /// </summary>
    public class AircraftReservationController : Controller
    {
        private const string TITLE = "機体更新の予約";

        /// <summary>処理済みを出す件数</summary>
        private const int HISTORY_COUNT = 30;

        private readonly JAFleetContext _context;

        public AircraftReservationController(JAFleetContext context) => _context = context;

        public async Task<IActionResult> IndexAsync([FromQuery] string? reg, [FromQuery] bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            AircraftReservationModel model = await BuildModelAsync(reg, fromAdmin);
            model.Message = TempData["ReservationMessage"] as string;
            model.ErrorMessage = TempData["ReservationError"] as string;
            return View("Index", model);
        }

        [HttpPost]
        public async Task<IActionResult> CancelAsync(int reservationId, string? reg, bool fromAdmin, string? returnTo)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            AircraftReservationStore.Result result = await AircraftReservationStore.CancelAsync(_context, reservationId);
            TempData["ReservationMessage"] = result.Message;
            TempData["ReservationError"] = result.Error;

            //単票編集の画面から取り消したときはそちらへ戻す
            if (returnTo == "E" && !string.IsNullOrEmpty(reg))
            {
                TempData["EMessage"] = result.Message;
                TempData["EError"] = result.Error;
                TempData.Remove("ReservationMessage");
                TempData.Remove("ReservationError");
                return Redirect($"/E/{reg}{(fromAdmin ? "?fromAdmin=true" : "")}");
            }
            return Redirect(ListUrl(reg, fromAdmin));
        }

        /// <summary>反映日が今日以前の予約を、日次ジョブを待たずにいま反映する</summary>
        [HttpPost]
        public async Task<IActionResult> ApplyNowAsync(string? reg, bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            List<AircraftReservationStore.ApplyOutcome>? outcomes = await ApplyAircraftReservationJob.RunAsync(_context);
            if (outcomes == null)
            {
                TempData["ReservationError"] = "反映が動いています。終わるまで待ってください。";
            }
            else if (outcomes.Count == 0)
            {
                TempData["ReservationMessage"] = "反映日が来ている予約はありませんでした。";
            }
            else
            {
                int applied = outcomes.Count(o => o.Reservation.Status == AircraftReservationStatus.APPLIED);
                int failed = outcomes.Count(o => o.Reservation.Status == AircraftReservationStatus.FAILED);
                TempData["ReservationMessage"] = $"予約{outcomes.Count}件を処理しました（反映{applied}件・失敗{failed}件）。";
            }
            return Redirect(ListUrl(reg, fromAdmin));
        }

        private static string ListUrl(string? reg, bool fromAdmin)
        {
            List<string> query = [];
            if (!string.IsNullOrEmpty(reg))
            {
                query.Add($"reg={Uri.EscapeDataString(reg)}");
            }
            if (fromAdmin)
            {
                query.Add("fromAdmin=true");
            }
            return "/AircraftReservation" + (query.Count == 0 ? "" : "?" + string.Join("&", query));
        }

        private async Task<AircraftReservationModel> BuildModelAsync(string? reg, bool fromAdmin)
        {
            string? filter = string.IsNullOrWhiteSpace(reg) ? null : reg.Trim().ToUpperInvariant();

            IQueryable<AircraftReservation> query = _context.AircraftReservations.AsNoTracking();
            if (filter != null)
            {
                query = query.Where(r => r.RegistrationNumber == filter);
            }

            List<AircraftReservation> pending = await query
                .Where(r => r.Status == AircraftReservationStatus.PENDING)
                .OrderBy(r => r.ApplyDate).ThenBy(r => r.ReservationId)
                .ToListAsync();
            List<AircraftReservation> resolved = await query
                .Where(r => r.Status != AircraftReservationStatus.PENDING)
                .OrderByDescending(r => r.ResolvedAt).ThenByDescending(r => r.ReservationId)
                .Take(HISTORY_COUNT)
                .ToListAsync();

            IReadOnlyDictionary<string, JobRuntimeState> states = await RootScheduler.GetStatesAsync();
            states.TryGetValue(typeof(ApplyAircraftReservationJob).FullName!, out JobRuntimeState? state);

            return new AircraftReservationModel
            {
                Title = TITLE,
                IsAdmin = true,
                FromAdmin = fromAdmin,
                Reg = filter,
                Pending = [.. pending.Select(ToRow)],
                Resolved = [.. resolved.Select(ToRow)],
                JobScheduled = state?.Scheduled ?? false,
                NextFireTime = state?.NextFireTime,
            };
        }

        public static AircraftReservationRow ToRow(AircraftReservation r) => new()
        {
            ReservationId = r.ReservationId,
            RegistrationNumber = r.RegistrationNumber,
            ApplyDate = r.ApplyDate,
            Changes = [.. AircraftReservationStore.Deserialize(r.Changes).Select(c => new AircraftReservationChangeRow(
                AircraftReservationStore.FieldName(c.Field),
                AircraftReservationStore.ValueText(c.Field, c.Before),
                AircraftReservationStore.ValueText(c.Field, c.After)))],
            WriteHistory = r.WriteHistory,
            Source = r.Source,
            Status = r.Status,
            CreatedAt = r.CreatedAt,
            ResolvedAt = r.ResolvedAt,
            ResultMessage = r.ResultMessage,
        };
    }
}
