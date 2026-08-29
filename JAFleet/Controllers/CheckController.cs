using JAFleet.Commons.Data;
using Microsoft.AspNetCore.Mvc;

namespace JAFleet.Controllers
{
    public class CheckController : Controller
    {
        private readonly JAFleetContext _context;

        public CheckController(JAFleetContext context) => _context = context;

        public IActionResult Index()
        {
            return Content(_context.AircraftViews.Count().ToString());
        }
    }
}