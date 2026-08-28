using Microsoft.AspNetCore.Mvc;
using JAFleet.Commons.EF;
using JAFleet.Commons.Constants;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TypeController : Controller
    {

        private readonly JAFleetContext _context;

        public TypeController(JAFleetContext context) => _context = context;

        // GET api/values
        [HttpGet]
        public ActionResult<IEnumerable<string>> Get()
        {
            List<AircraftView> list;
            list = _context.AircraftViews.AsNoTracking().OrderBy(p => p.DisplayOrder).ToList();
            return Json(list);
        }

        // GET api/values/5
        [HttpGet("{id}")]
        public ActionResult<string> Get(string id, [FromQuery] bool includeRetire)
        {
            List<AircraftView> list;
            string[] ids = id.ToUpper().Split(",");
            var q = _context.AircraftViews.AsNoTracking().Where(p => ids.Contains(p.TypeCode));
            if (!includeRetire)
            {
                q = q.Where(p => p.OperationCode != OperationCode.RETIRE_UNREGISTERED);
            }
            list = q.OrderBy(p => p.DisplayOrder).ToList();

            return Json(list);
        }

    }
}
