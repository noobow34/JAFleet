using Microsoft.AspNetCore.Mvc;
using JAFleet.Commons.EF;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AircraftWithHistoryController : Controller
    {

        private readonly JAFleetContext _context;

        public AircraftWithHistoryController(JAFleetContext context) => _context = context;

        // GET api/values/5
        [HttpGet("{id}")]
        public ActionResult<string> Get(string id)
        {
            var list = new List<AircraftViewBase>();
            var latest = _context.AircraftViews.AsNoTracking().Where(p => p.RegistrationNumber == id.ToUpper()).First();
            var history = _context.AircraftHistoryViews.AsNoTracking().Where(p => p.RegistrationNumber == id.ToUpper()).OrderByDescending(p => p.Seq).ToList();

            list.Add(latest);
            list.AddRange(history);

            for (int i = 0; i <= list.Count - 2; i++)
            {
                list[i].getDifferenceWith(list[i + 1]);
            }

            return Json(list);
        }

    }
}
