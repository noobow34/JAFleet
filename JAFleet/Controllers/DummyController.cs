using Microsoft.AspNetCore.Mvc;

namespace JAFleet.Controllers
{
    public class DummyController : Controller
    {
        public IActionResult Index()
        {
            return Content(string.Empty);
        }
    }
}