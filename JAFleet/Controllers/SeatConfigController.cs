using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using JAFleet.Models;
using JAFleet.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Type = JAFleet.Commons.Data.Type;

namespace JAFleet.Controllers
{
    /// <summary>
    /// コンフィグ（seat_configuration）のマスタ画面。
    /// 航空会社・型式ごとの座席仕様を登録しておき、単票編集（/E）と一括登録はここから選ぶ。
    /// 保存するとマスタのメモリキャッシュも読み直すので、/ReloadMasterを押す必要はない。
    /// </summary>
    public class SeatConfigController : Controller
    {
        private const string TITLE = "コンフィグマスタ";

        private readonly JAFleetContext _context;

        public SeatConfigController(JAFleetContext context) => _context = context;

        public async Task<IActionResult> IndexAsync([FromQuery] string? airline, [FromQuery] string? type, [FromQuery] bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            return View(await BuildModelAsync(airline, type, fromAdmin));
        }

        /// <summary>1件分を登録または更新する。ConfigIdが無ければ新規登録。</summary>
        [HttpPost]
        public async Task<IActionResult> SaveAsync(SeatConfigInput input, string? filterAirline, string? filterType, bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            SeatConfigStore.Result result = SeatConfigStore.Save(_context, input);
            SeatConfigModel model = await BuildModelAsync(filterAirline, filterType, fromAdmin);

            if (result.Error == null)
            {
                model.Message = result.Message;
                return View("Index", model);
            }

            //入力し直せるように、保存できなかった内容を画面に残す
            model.ErrorMessage = result.Error;
            if (input.ConfigId == null)
            {
                model.New = input;
            }
            else
            {
                model.ErrorConfigId = input.ConfigId;
                SeatConfigRow? row = model.Rows.FirstOrDefault(r => r.ConfigId == input.ConfigId);
                if (row != null)
                {
                    row.Airline = input.Airline;
                    row.Type = input.Type;
                    row.Url = input.Url;
                    row.F = input.F;
                    row.C = input.C;
                    row.PY = input.PY;
                    row.P = input.P;
                    row.J = input.J;
                    row.Y = input.Y;
                }
            }
            return View("Index", model);
        }

        /// <summary>使っている機体が無いコンフィグを削除する。</summary>
        [HttpPost]
        public async Task<IActionResult> DeleteAsync(int configId, string? filterAirline, string? filterType, bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            SeatConfigStore.Result result = SeatConfigStore.Delete(_context, configId);
            SeatConfigModel model = await BuildModelAsync(filterAirline, filterType, fromAdmin);

            if (result.Error == null)
            {
                model.Message = result.Message;
            }
            else
            {
                model.ErrorMessage = result.Error;
                model.ErrorConfigId = configId;
            }
            return View("Index", model);
        }

        private async Task<SeatConfigModel> BuildModelAsync(string? airline, string? type, bool fromAdmin)
        {
            airline = string.IsNullOrEmpty(airline?.Trim()) ? null : airline!.Trim();
            type = string.IsNullOrEmpty(type?.Trim()) ? null : type!.Trim();

            List<SeatConfiguration> all = await _context.SeatConfigurations.AsNoTracking().ToListAsync();

            //どのコンフィグが何機に使われているか。削除してよいかの判断に使う。
            Dictionary<int, int> used = await _context.Aircrafts.AsNoTracking()
                .Where(a => a.SeatConfig != null)
                .GroupBy(a => a.SeatConfig!.Value)
                .Select(g => new { ConfigId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ConfigId, x => x.Count);

            //削除済みの航空会社やマスタから消えた型式の行も出すので、名前はDBの全件から引く
            Dictionary<string, Airline> airlines = await _context.Airlines.AsNoTracking()
                .ToDictionaryAsync(a => a.AirlineCode, a => a);
            Dictionary<string, Type> types = await _context.Types.AsNoTracking()
                .ToDictionaryAsync(t => t.TypeCode, t => t);

            IEnumerable<SeatConfiguration> filtered = all;
            if (airline != null)
            {
                filtered = filtered.Where(sc => sc.Airline == airline);
            }
            if (type != null)
            {
                filtered = filtered.Where(sc => sc.Type == type);
            }

            List<SeatConfigRow> rows = [.. filtered
                .OrderBy(sc => DisplayOrder(airlines, sc.Airline))
                .ThenBy(sc => sc.Airline)
                .ThenBy(sc => DisplayOrder(types, sc.Type))
                .ThenBy(sc => sc.Type)
                .ThenBy(sc => sc.ConfigId)
                .Select(sc => new SeatConfigRow
                {
                    ConfigId = sc.ConfigId!.Value,
                    Airline = sc.Airline,
                    AirlineName = Name(airlines, sc.Airline),
                    Type = sc.Type,
                    TypeName = Name(types, sc.Type),
                    Url = sc.Url,
                    F = sc.F,
                    C = sc.C,
                    PY = sc.PY,
                    P = sc.P,
                    J = sc.J,
                    Y = sc.Y,
                    SeatConfig = sc.SeatConfig,
                    UsedCount = used.TryGetValue(sc.ConfigId!.Value, out int count) ? count : 0,
                })];

            return new SeatConfigModel
            {
                Title = TITLE,
                IsAdmin = true,
                FromAdmin = fromAdmin,
                Rows = rows,
                AirlineList = MasterManager.AllAirline ?? [],
                TypeList = MasterManager.Type ?? [],
                FilterAirline = airline,
                FilterType = type,
                TotalCount = all.Count,
            };
        }

        /// <summary>並び順はマスタのdisplay_orderに合わせる。マスタに無いコードは最後に回す。</summary>
        private static string DisplayOrder(Dictionary<string, Airline> airlines, string? code)
            => code != null && airlines.TryGetValue(code, out Airline? a) ? a.DisplayOrder ?? "zzz" : "zzz";

        private static string DisplayOrder(Dictionary<string, Type> types, string? code)
            => code != null && types.TryGetValue(code, out Type? t) ? t.DisplayOrder ?? "zzz" : "zzz";

        private static string Name(Dictionary<string, Airline> airlines, string? code)
            => code != null && airlines.TryGetValue(code, out Airline? a) ? a.AirlineNameJpShort ?? code : code ?? string.Empty;

        private static string Name(Dictionary<string, Type> types, string? code)
            => code != null && types.TryGetValue(code, out Type? t) ? t.TypeName ?? code : code ?? string.Empty;
    }
}
