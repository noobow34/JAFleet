using JAFleet.Commons.Data;
using JAFleet.StandingData;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Batch
{
    /// <summary>
    /// 路線（flight_route）と空港（airport）を VRS standing-data で丸ごと入れ替える。
    /// 取れた件数が少なすぎるとき（配布物が壊れている・形式が変わった）は入れ替えずに止める。
    /// </summary>
    public class RefreshFlightRoute(DbContextOptions<JAFleetContext> options, HttpClient http)
    {
        /// <summary>これより少なければ入れ替えない（2026-11 時点で空港 34,128 件、日本の路線 約6,000件）</summary>
        public const int MIN_AIRPORTS = 20000;
        public const int MIN_ROUTES = 3000;

        public async Task<string> ExecuteAsync(CancellationToken ct = default)
        {
            var now = DateTime.Now;
            var data = await VrsStandingData.DownloadAsync(http, now, ct);
            string? problem = Check(data.Airports.Count, data.Routes.Count);
            if (problem != null)
            {
                return $"路線・空港の更新を中止しました: {problem}（DB は書き換えていません）";
            }

            using var context = new JAFleetContext(options);
            int beforeRoutes = await context.FlightRoutes.CountAsync(ct);
            int beforeAirports = await context.Airports.CountAsync(ct);
            using (var tx = await context.Database.BeginTransactionAsync(ct))
            {
                await context.FlightRoutes.ExecuteDeleteAsync(ct);
                await context.Airports.ExecuteDeleteAsync(ct);
                context.Airports.AddRange(data.Airports);
                context.FlightRoutes.AddRange(data.Routes);
                await context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }

            var perAirline = data.Routes.GroupBy(r => r.AirlineIcao).OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} {g.Count()}");
            return $"路線・空港を更新しました（VRS standing-data）\n" +
                $"路線 {beforeRoutes} → {data.Routes.Count} 件、空港 {beforeAirports} → {data.Airports.Count} 件\n" +
                $"航空会社別: {string.Join(", ", perAirline)}";
        }

        /// <summary>入れ替えてよい件数か。だめなら理由を返す</summary>
        public static string? Check(int airports, int routes)
        {
            if (airports < MIN_AIRPORTS)
            {
                return $"空港が {airports} 件しか取れなかった（{MIN_AIRPORTS} 件未満）";
            }
            if (routes < MIN_ROUTES)
            {
                return $"路線が {routes} 件しか取れなかった（{MIN_ROUTES} 件未満）";
            }
            return null;
        }
    }
}
