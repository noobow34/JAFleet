using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace JAFleet.Controllers
{
    public class LineController : Controller
    {
        private readonly IServiceScopeFactory _services;
        public LineController(IServiceScopeFactory serviceScopeFactory) => _services = serviceScopeFactory;

        public IActionResult Index()
        {
            //速くリダイレクトするため、ログの書き込みは非同期
            Task.Run(() =>
            {
                using var serviceScope = _services.CreateScope();
                using JAFleetContext context = serviceScope.ServiceProvider.GetService<JAFleetContext>()!;
                var lineLinklog = new Log
                {
                    LogType = LogType.LINE_LINK,
                    UserId = CookieUtil.IsAdmin(HttpContext).ToString(),
                    LogDate = DateTime.Now
                };

                context.Logs.Add(lineLinklog);
                context.SaveChanges();
            });
            return Redirect("https://line.me/R/ti/p/BTy1CuBCzF");
        }
    }
}