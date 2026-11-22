using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using JAFleet.Models;
using Microsoft.AspNetCore.Diagnostics;
using JAFleet.Infrastructure;
using JAFleet.Commons.Data;
using JAFleet.Commons.Constants;
using Noobow.Commons.Utils;
using Noobow.Commons.Constants;
using EnumStringValues;

namespace JAFleet.Controllers
{
    public class HomeController : Controller
    {

        private readonly JAFleetContext _context;
        private readonly IConfiguration _configuration;

        public HomeController(JAFleetContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public IActionResult Index()
        {
            //トップに出す機数。一覧の初期表示（抹消済を除く）と同じ数え方にする
            var active = _context.AircraftViews.AsNoTracking().Where(p => p.OperationCode != OperationCode.RETIRE_UNREGISTERED);
            HomeModel model = new()
            {
                AirlineCounts = active.GroupBy(p => p.Airline).Select(g => new { g.Key, Count = g.Count() })
                    .ToList().Where(x => x.Key != null).ToDictionary(x => x.Key!, x => x.Count),
                TypeCounts = active.GroupBy(p => p.TypeCode).Select(g => new { g.Key, Count = g.Count() })
                    .ToList().Where(x => x.Key != null).ToDictionary(x => x.Key!, x => x.Count),
            };
            return View(model);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> ErrorAsync()
        {
            var ex = HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Error!;
            ErrorViewModel model = new() { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier };
            model.IsAdmin = AdminAuth.IsAdmin(HttpContext);
            model.Ex = ex;

            await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), $"【エラー発生】\n" +
                            $"{ex.ToString().Split(Environment.NewLine)[0]}\n" +
                            $"{ex.ToString().Split(Environment.NewLine)[1]}");

            Log log = new()
            {
                LogDate = DateTime.Now
                ,
                LogType = LogType.EXCEPTION
                ,
                LogDetail = ex.ToString()
                ,
                UserId = AdminAuth.IsAdmin(HttpContext).ToString()
            };
            _context.Logs.Add(log);
            _context.SaveChanges();

            return View(model);
        }
    }
}
