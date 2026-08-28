using Microsoft.AspNetCore.Mvc;

namespace JAFleet.Controllers
{
    public class CheckErrorController : Controller
    {
        public IActionResult Index()
        {
            return new NotFoundResult();
        }
    }
}