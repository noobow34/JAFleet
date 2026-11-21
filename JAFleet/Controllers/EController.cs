using Microsoft.AspNetCore.Mvc;
using JAFleet.Services;
using JAFleet.Models;
using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using Microsoft.EntityFrameworkCore;
using JAFleet.Commons.Constants;

namespace JAFleet.Controllers
{
    public class EController : Controller
    {

        private readonly JAFleetContext _context;

        public EController(JAFleetContext context) => _context = context;

        public IActionResult Index(string id, EditModel model, [FromQuery] bool nohead)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            model.AirlineList = MasterManager.AllAirline!;
            model.TypeList = MasterManager.Type!;
            model.TypeDetailList = _context.TypeDetails.OrderBy(t => t.TypeDetailName).ToArray();
            model.OperationList = MasterManager.Operation!;
            model.WiFiList = MasterManager.Wifi!;
            model.NotUpdateDate = true;
            model.NoHead = nohead;

            if (string.IsNullOrEmpty(id))
            {
                model.Aircraft = new Aircraft();
                model.IsNew = true;
            }
            else
            {
                model.Aircraft = _context.Aircrafts.Where(p => p.RegistrationNumber == id.ToUpper()).FirstOrDefault();
            }

            if (model.Aircraft == null)
            {
                model.Aircraft = new Aircraft();
                model.Aircraft.RegistrationNumber = id.ToUpper();
                model.IsNew = true;
                model.LinkPage = $"https://ja-fleet.noobow.me/AD/{id.ToUpper()}";
            }
            //レジ無しで開いた新規登録（/E）はここに来る。idが無いので機体は引けない。
            else if (!string.IsNullOrEmpty(id))
            {
                AircraftView? av = _context.AircraftViews.Where(av => av.RegistrationNumber == id.ToUpper()).SingleOrDefault();
                if (av != null) 
                {
                    model.LinkPage = $"https://ja-fleet.noobow.me/AD/{av.RegistrationNumber}";
                }
            }
            var type = MasterManager.TypeDetailGroup?.Where(td => td.TypeDetailId == model.Aircraft.TypeDetailId).FirstOrDefault()?.TypeCode;
            IEnumerable<SeatConfiguration>? q = MasterManager.SeatConfiguration;
            if (!string.IsNullOrEmpty(model.Aircraft.Airline))
            {
                q = q?.Where(sc => sc.Airline == model.Aircraft.Airline);
            }
            if (!string.IsNullOrEmpty(type))
            {
                q = q?.Where(sc => sc.Type == type);
            }
            model.SeatConfigurationList = q?.ToArray();

            if (!model.IsNew)
            {
                string reg = model.Aircraft.RegistrationNumber!;
                model.Reservations = [.. _context.AircraftReservations.AsNoTracking()
                    .Where(r => r.RegistrationNumber == reg && r.Status == AircraftReservationStatus.PENDING)
                    .OrderBy(r => r.ApplyDate).ThenBy(r => r.ReservationId)
                    .ToList()
                    .Select(AircraftReservationController.ToRow)];
            }
            model.Message = TempData["EMessage"] as string;
            model.ErrorMessage = TempData["EError"] as string;

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Store(EditModel model)
        {
            //予約日が入っていれば、いまは更新せずに予約だけする
            if (model.ReserveDate != null && !model.IsNew)
            {
                AircraftReservationStore.Result reserved;
                try
                {
                    reserved = await AircraftReservationStore.ReserveFromEditAsync(
                        _context, model.Aircraft!, model.ReserveDate, !model.NotUpdateDate, DateTime.Now);
                }
                catch (Exception ex)
                {
                    reserved = new AircraftReservationStore.Result { Error = $"予約できませんでした。({ex.GetBaseException().Message})" };
                }
                TempData["EMessage"] = reserved.Message;
                TempData["EError"] = reserved.Error;
                return Redirect("/E/" + model.Aircraft!.RegistrationNumber + QueryString(model));
            }

            try
            {
                AircraftStore.Store(_context, model.Aircraft!, model.IsNew, !model.NotUpdateDate, DateTime.Now);
                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                model.ex = ex;
            }

            model.AirlineList = MasterManager.AllAirline!;
            model.TypeList = MasterManager.Type!;
            model.OperationList = MasterManager.Operation!;
            model.WiFiList = MasterManager.Wifi!;
            string noheadString = QueryString(model);

            //写真を更新
            _ = HttpClientManager.GetInstance().GetStringAsync($"http://localhost:5000/Aircraft/Photo/{model.Aircraft!.RegistrationNumber}?force=true");

            return Redirect("/E/" + model.Aircraft.RegistrationNumber + noheadString);
        }

        private static string QueryString(EditModel model)
        {
            List<string> query = [];
            if (model.NoHead)
            {
                query.Add("nohead=true");
            }
            if (model.FromAdmin)
            {
                query.Add("fromAdmin=true");
            }
            return query.Count == 0 ? string.Empty : "?" + string.Join("&", query);
        }
    }
}
