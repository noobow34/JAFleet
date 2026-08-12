using Microsoft.AspNetCore.Mvc;
using jafleet.Classes;
using jafleet.Manager;
using jafleet.Models;
using jafleet.Commons.EF;
using jafleet.Util;
using Microsoft.EntityFrameworkCore;

namespace jafleet.Controllers
{
    public class EController : Controller
    {

        private readonly JafleetContext _context;

        public EController(JafleetContext context) => _context = context;

        public IActionResult Index(string id, EditModel model, [FromQuery] bool nohead)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
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
            else
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

            return View(model);
        }

        [HttpPost]
        public IActionResult Store(EditModel model)
        {
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
            List<string> query = [];
            if (model.NoHead)
            {
                query.Add("nohead=true");
            }
            if (model.FromAdmin)
            {
                query.Add("fromAdmin=true");
            }
            string noheadString = query.Count == 0 ? string.Empty : "?" + string.Join("&", query);

            //写真を更新
            _ = HttpClientManager.GetInstance().GetStringAsync($"http://localhost:5000/Aircraft/Photo/{model.Aircraft!.RegistrationNumber}?force=true");

            return Redirect("/E/" + model.Aircraft.RegistrationNumber + noheadString);
        }
    }
}
