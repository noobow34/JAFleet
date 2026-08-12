using Auth0.AspNetCore.Authentication;
using jafleet;
using jafleet.Classes;
using jafleet.Commons.EF;
using jafleet.Manager;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.WebEncoders;
using System.Text.Encodings.Web;
using System.Text.Unicode;

Console.WriteLine($"SLACK_BOT_TOKEN:{Environment.GetEnvironmentVariable("SLACK_BOT_TOKEN")?.Length ?? 0}");
string connectionString = Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "";
Console.WriteLine($"JAFLEET_CONNECTION_STRING:{connectionString?.Length ?? 0}");
string auth0Domain = Environment.GetEnvironmentVariable("AUTH0_DOMAIN") ?? "";
string auth0ClientId = Environment.GetEnvironmentVariable("AUTH0_CLIENT_ID") ?? "";
Console.WriteLine($"AUTH0_ISSUER:{auth0Domain.Length}");
Console.WriteLine($"AUTH0_CLIENT_ID:{auth0ClientId.Length}");
string adminKey = Environment.GetEnvironmentVariable("ADMIN_KEY") ?? "";
Console.WriteLine($"ADMIN_KEY:{adminKey.Length}");
string adminValue = Environment.GetEnvironmentVariable("ADMIN_VALUE") ?? "";
Console.WriteLine($"ADMIN_VALUE:{adminValue.Length}");

//EPPlusは非商用ライセンス。設定しないと初回利用時に例外になる
OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("noobow34");

var builder = WebApplication.CreateBuilder(args);
var config = new ConfigurationBuilder().SetBasePath(Environment.CurrentDirectory).AddJsonFile("appsettings.json").Build();

builder.Services.AddDbContextPool<JafleetContext>(
    options => options.UseNpgsql(connectionString)
);
builder.Services.Configure<WebEncoderOptions>(options =>
{
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All);
});
builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();
builder.Services.AddProgressiveWebApp();
builder.Services.AddAuth0WebAppAuthentication(options =>
{
    options.Domain = auth0Domain;
    options.ClientId = auth0ClientId;
});
builder.Services.AddSingleton<IConfiguration>(config);

var app = builder.Build();

app.UseExceptionHandler("/Home/Error");

app.UseLoggingMiddleware();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<ConditionalAuthRedirectMiddleware>();
app.UseAuthorization();
app.MapControllerRoute(
    name: "EditStore",
    pattern: "E/Store",
    defaults: new { controller = "E", action = "Store" }
);
app.MapControllerRoute(
    name: "Edit1",
    pattern: "e/{id?}",
    defaults: new { controller = "E", action = "Index" }
);
app.MapControllerRoute(
    name: "Edit2",
    pattern: "E/{id?}",
    defaults: new { controller = "E", action = "Index" }
);
app.MapControllerRoute(
    name: "Log",
    pattern: "log/{id?}",
    defaults: new { controller = "log", action = "Index" }
);
app.MapControllerRoute(
    name: "AircraftDetail1",
    pattern: "AircraftDetail/{id?}",
    defaults: new { controller = "AircraftDetail", action = "Index" }
);
app.MapControllerRoute(
    name: "AircraftDetail2",
    pattern: "AD/{id?}",
    defaults: new { controller = "AircraftDetail", action = "Index" }
);
app.MapControllerRoute(
    name: "AircraftDetail3",
    pattern: "ADN/{id?}",
    defaults: new { controller = "AircraftDetail", action = "IndexNohead" }
);
app.MapControllerRoute(
    name: "AircraftDetail4",
    pattern: "ADNB/{id?}",
    defaults: new { controller = "AircraftDetail", action = "IndexNoheadBack" }
);
app.MapControllerRoute(
    name: "AircraftDetail4",
    pattern: "ADE/{id?}",
    defaults: new { controller = "AircraftDetail", action = "Emb" }
);
app.MapControllerRoute(
    name: "Logy",
    pattern: "logy",
    defaults: new { controller = "log", action = "Yesterday" }
);
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}/{id2?}"
);

var options = new DbContextOptionsBuilder<JafleetContext>();
options.UseNpgsql(connectionString);
using JafleetContext context = new(options.Options);
MasterManager.ReadAll(context);

RootScheduler.CreateOrReloadRootScheduler();

app.Run();