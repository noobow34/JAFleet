namespace JAFleet.Infrastructure
{
    public static class AdminAuth
    {
        public static bool IsAdmin(HttpContext context)
        {
            return context.User.Identity?.IsAuthenticated ?? false;
        }
    }
}
