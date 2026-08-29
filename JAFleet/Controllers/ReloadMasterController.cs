using Microsoft.AspNetCore.Mvc;
using JAFleet.Services;
using JAFleet.Commons.Data;

namespace JAFleet.Controllers
{
    public class ReloadMasterController : Controller
    {
        private readonly JAFleetContext _context;

        public ReloadMasterController(JAFleetContext context) => _context = context;

        public IActionResult Index()
        {
            try
            {
                MasterManager.ReadAll(_context);
                return Content("Success");
            }
            catch (Exception ex)
            {
                return Content(ex.ToString());
            }
        }
    }
}
