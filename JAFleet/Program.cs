using JAFleet.Infrastructure;
using JAFleet.Jobs;
using JAFleet.Middleware;
using JAFleet.Services;
using JAFleet.Commons.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.WebEncoders;
using System.Text.Encodings.Web;
using System.Text.Unicode;

Console.WriteLine($"SLACK_BOT_TOKEN:{Environment.GetEnvironmentVariable("SLACK_BOT_TOKEN")?.Length ?? 0}");
string connectionString = Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "";
Console.WriteLine($"JAFLEET_CONNECTION_STRING:{connectionString?.Length ?? 0}");
string cfAccessTeamDomain = Environment.GetEnvironmentVariable("CF_ACCESS_TEAM_DOMAIN") ?? "";
string cfAccessAud = Environment.GetEnvironmentVariable("CF_ACCESS_AUD") ?? "";
Console.WriteLine($"CF_ACCESS_TEAM_DOMAIN:{cfAccessTeamDomain.Length}");
Console.WriteLine($"CF_ACCESS_AUD:{cfAccessAud.Length}");
string adminKey = Environment.GetEnvironmentVariable("ADMIN_KEY") ?? "";
Console.WriteLine($"ADMIN_KEY:{adminKey.Length}");
string adminValue = Environment.GetEnvironmentVariable("ADMIN_VALUE") ?? "";
Console.WriteLine($"ADMIN_VALUE:{adminValue.Length}");

//EPPlusは非商用ライセンス。設定しないと初回利用時に例外になる
OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("noobow34");

var builder = WebApplication.CreateBuilder(args);
var config = new ConfigurationBuilder().SetBasePath(Environment.CurrentDirectory).AddJsonFile("appsettings.json").Build();

builder.Services.AddDbContextPool<JAFleetContext>(
    options => options.UseNpgsql(connectionString)
);
builder.Services.Configure<WebEncoderOptions>(options =>
{
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All);
});
builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();
builder.Services.AddProgressiveWebApp();
//DataProtectionのApplicationDiscriminatorは既定でContentRootPathから導出される。
//デプロイのたびにreleases/<timestamp>-<sha>/へ変わり保護済みデータ(CSRFトークン、TempData)が
//復号できなくなるため、アプリ名を固定する。
//キーリング自体は従来どおり~/.aspnet/DataProtection-Keysに永続化される
builder.Services.AddDataProtection().SetApplicationName("ja-fleet");
//管理者認証はCloudflare Accessが行い、アプリはAccessが発行したJWTを検証するだけ
builder.Services.AddCloudflareAccess(cfAccessTeamDomain, cfAccessAud);
builder.Services.AddSingleton<IConfiguration>(config);

var app = builder.Build();

app.UseExceptionHandler("/Home/Error");

app.UseLoggingMiddleware();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
#if DEBUG
//手元での実行ではCloudflareを経由せずAccessのJWTが手に入らないため、明示的に指定したときだけ管理者になりすます。
//本番は -c Release で publish するのでこのブロックごとバイナリに入らない。
//ASPNETCORE_ENVIRONMENTを取り違えても発火しようがない状態にしておく
if (app.Environment.IsDevelopment() && Environment.GetEnvironmentVariable("CF_ACCESS_DEV_ADMIN") == "1")
{
    app.UseCloudflareAccessDevAdmin();
}
#endif
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
    defaults: new { controller = "Log", action = "Index" }
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
    defaults: new { controller = "Log", action = "Yesterday" }
);
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}/{id2?}"
);

var options = new DbContextOptionsBuilder<JAFleetContext>();
options.UseNpgsql(connectionString);
using JAFleetContext context = new(options.Options);
MasterManager.ReadAll(context);

await RootScheduler.CreateOrReloadRootScheduler();

app.Run();