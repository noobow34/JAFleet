using Microsoft.AspNetCore.Mvc;
using JAFleet.Commons.Data;
using JAFleet.Commons.Constants;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AirlineController : Controller
    {

        private readonly JAFleetContext _context;

        public AirlineController(JAFleetContext context) => _context = context;

        // GET api/values
        [HttpGet]
        public ActionResult<IEnumerable<string>> Get()
        {
            List<AircraftView> list;
            list = _context.AircraftViews.AsNoTracking().OrderBy(p => p.DisplayOrder).ToList();

            return Json(list);
        }

        // GET api/values/5
        [HttpGet("{id}/{id2?}")]
        public ActionResult<string> Get(string? id, string? id2, [FromQuery] bool includeRetire)
        {
            List<AircraftView> list;
            string[] ids = id?.ToUpper().Split(",")!;
            id2 = id2?.ToUpper();
            var q = _context.AircraftViews.AsNoTracking().Where(p => ids.Contains(p.Airline));
            if (!string.IsNullOrEmpty(id2))
            {
                q = q.Where(p => p.TypeCode == id2);
            }
            if (!includeRetire)
            {
                q = q.Where(p => p.OperationCode != OperationCode.RETIRE_UNREGISTERED);
            }
            list = q.OrderBy(p => p.DisplayOrder).ToList();

            return Json(list);
        }

    }
}
