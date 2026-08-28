namespace JAFleet.Util
{
    public static class CookieUtil
    {
        public static bool IsAdmin(HttpContext context)
        {
            return context.User.Identity?.IsAuthenticated ?? false;
        }
    }
}
