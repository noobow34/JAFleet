using Auth0.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace JAFleet.Middleware
{
    public class ConditionalAuthRedirectMiddleware
    {
        private readonly RequestDelegate _next;
        private static string[] EXCLUDE_LIST = [".CSS", ".JS", ".PNG", ".JPG", ".JPEG", ".GIF", ".ICO", "/CHECK", "/ACCOUNT/LOGIN", "/SETCOOKIE", "/API", "/MASTER", "/LOG"];
        private static readonly string adminKey = Environment.GetEnvironmentVariable("ADMIN_KEY") ?? "";
        private static readonly string adminValue = Environment.GetEnvironmentVariable("ADMIN_VALUE") ?? "";

        public ConditionalAuthRedirectMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            bool autoLoginTarget = !EXCLUDE_LIST.Any(s => context.Request.Path.Value!.Contains(s, StringComparison.CurrentCultureIgnoreCase));
            if (context.User.Identity!.IsAuthenticated || !autoLoginTarget)
            {
                await _next(context);
                return;
            }

            context.Request.Cookies.TryGetValue(adminKey, out string? adminCookieValue);
            if (adminCookieValue == adminValue)
            {
                string returnUrl = context.Request.Path;
                context.Response.Cookies.Append(adminKey, adminCookieValue, new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1)
                });
                var authenticationProperties = new LoginAuthenticationPropertiesBuilder()
                    .WithRedirectUri(returnUrl)
                    .Build();
                await context.ChallengeAsync(Auth0Constants.AuthenticationScheme, authenticationProperties);
                return;
            }
            
            await _next(context);
        }
    }
}
