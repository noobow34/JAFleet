using Microsoft.AspNetCore.Mvc;

namespace JAFleet.Controllers
{
    public class SetCookieController : Controller
    {
        public IActionResult Index(string key,string value)
        {
            if(string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
            {
                return BadRequest("Key or Value is null or empty.");
            }

            HttpContext.Response.Cookies.Append(key, value, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(365)
            });

            return Content("OK");
        }
    }
}
