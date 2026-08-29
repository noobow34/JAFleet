using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using JAFleet.Models;
using JAFleet.Commons.Data;
using JAFleet.Services;
using JAFleet.Infrastructure;
using Microsoft.EntityFrameworkCore;
using JAFleet.Commons.Constants;

namespace JAFleet.Controllers
{
    public class AircraftController : Controller
    {

        private readonly JAFleetContext _context;
        private readonly IServiceScopeFactory _services;

        public AircraftController(JAFleetContext context, IServiceScopeFactory serviceScopeFactory)
        {
            _context = context;
            _services = serviceScopeFactory;
        }

        public IActionResult Index(AircraftModel model)
        {

            model.Title = "all";
            model.TableId = "all";
            model.api = "/api/airlinegroup/";

            model.IsAdmin = CookieUtil.IsAdmin(HttpContext);

            return View("~/Views/Aircraft/index.cshtml", model);
        }

        public IActionResult AirlineGroup(string? id, string? id2, [FromQuery] bool includeRetire, AircraftModel model)
        {
            id = id?.ToUpper();
            id2 = id2?.ToUpper();

            string? groupName;
            groupName = _context.AirlineGroups.AsNoTracking().FirstOrDefault(p => p.AirlineGroupCode == id)?.AirlineGroupName;

            model.Title = groupName ?? "all";
            model.TableId = id ?? "all";
            model.api = "/api/airlinegroup/" + id;
            if (!string.IsNullOrEmpty(id2))
            {
                var typeName = MasterManager.Type?.Where(p => p.TypeCode == id2).FirstOrDefault()?.TypeName;
                if (!string.IsNullOrEmpty(typeName))
                {
                    model.Title += ("・" + typeName);
                    model.api += ("/" + id2);
                }
            }

            model.IncludeRetire = includeRetire;
            model.IsAdmin = CookieUtil.IsAdmin(HttpContext);

            return View("~/Views/Aircraft/index.cshtml", model);
        }

        public IActionResult Airline(string? id, string? id2, [FromQuery] bool includeRetire, AircraftModel model)
        {
            id = id?.ToUpper();
            id2 = id2?.ToUpper();

            string? airlineName;
            airlineName = _context.Airlines.AsNoTracking().FirstOrDefault(p => p.AirlineCode == id)?.AirlineNameJpShort;

            model.Title = airlineName ?? "all";
            model.TableId = id ?? "all";
            model.api = "/api/airline/" + id;
            if (!string.IsNullOrEmpty(id2))
            {
                var typeName = MasterManager.Type?.Where(p => p.TypeCode == id2).FirstOrDefault()?.TypeName;
                if (!string.IsNullOrEmpty(typeName))
                {
                    model.Title += ("・" + typeName);
                    model.api += ("/" + id2);
                }
            }

            model.IncludeRetire = includeRetire;
            model.IsAdmin = CookieUtil.IsAdmin(HttpContext);

            return View("~/Views/Aircraft/index.cshtml", model);
        }

        public IActionResult Type(string? id, [FromQuery] bool? includeRetire, AircraftModel model)
        {
            id = id?.ToUpper();

            string? typeName;
            typeName = _context.Types.AsNoTracking().FirstOrDefault(p => p.TypeCode == id)?.TypeName;

            model.Title = typeName ?? "all";
            model.TableId = id ?? "all";
            model.api = "/api/type/" + id;

            //全機退役かどうか確認
            int operatingCount = _context.AircraftViews.AsNoTracking().Where(p => p.TypeCode == id && p.OperationCode != OperationCode.RETIRE_UNREGISTERED).Count();

            if (operatingCount == 0 && !includeRetire.HasValue)
            {
                //全機退役済みなら強制的に退役済みを表示
                model.IncludeRetire = true;
                model.IsAllRetire = true;
            }
            else
            {
                model.IncludeRetire = includeRetire.HasValue ? includeRetire.Value : false;
            }

            model.IsAdmin = CookieUtil.IsAdmin(HttpContext);

            return View("~/Views/Aircraft/index.cshtml", model);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
