using JAFleet.Adsb;
using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using JAFleet.Jobs;
using Microsoft.EntityFrameworkCore;
using Noobow.Commons.Extensions;

namespace JAFleet.Batch
{
    /// <summary>
    /// 機体記号と ICAO 24bit アドレス（hex）の対応（aircraft_hex）のうち、まだ hex の無い機体（退役と予約登録を除く）を
    /// ADSBdb で1機ずつ引いて埋める。
    /// ダンプのレジで覚えるのは毎日の RefreshAdsbFlightJob が取り込みのついでにやるので、ここではダンプを取らない
    /// （約3GiB をもう一度落とすのは無駄なため）。新造機など、ダンプにレジ付きで出てこない機体を補うためのもの。
    /// Learn はダンプから覚えるときの処理で、RefreshAdsbFlight から呼ぶ。
    /// </summary>
    public class RefreshAircraftHex
    {
        /// <summary>ADSBdb を続けて叩くときの間隔</summary>
        public static TimeSpan AdsbdbInterval { get; set; } = TimeSpan.FromSeconds(2);

        public static bool Processing { get; private set; }

        private readonly DbContextOptions<JAFleetContext> _options;
        private readonly HttpClient _http;

        public RefreshAircraftHex(DbContextOptions<JAFleetContext> options, HttpClient http)
        {
            _options = options;
            _http = http;
        }

        /// <summary>hex の無い機体を ADSBdb で引く。Slack には流さない（呼び出し側が ToSlackMessage で流す）</summary>
        public async Task<HexRefreshResult> ExecuteAsync(CancellationToken ct = default)
        {
            Processing = true;
            try
            {
                var result = new HexRefreshResult();
                await FillFromAdsbdbAsync(result, ct);
                return result;
            }
            finally
            {
                Processing = false;
                JobActivity.Clear(typeof(RefreshAircraftHexJob));
            }
        }

        private async Task FillFromAdsbdbAsync(HexRefreshResult result, CancellationToken ct)
        {
            using var context = new JAFleetContext(_options);
            var known = context.AircraftHexes.AsNoTracking().Select(h => h.RegistrationNumber).ToHashSet(StringComparer.Ordinal);
            var targets = context.Aircrafts.AsNoTracking()
                //予約登録だけの機体はまだ飛んでいないので引かない（飛び始めればダンプのほうで覚える）
                .Where(a => a.OperationCode == null
                    || (!OperationCode.RETIRE.Contains(a.OperationCode) && a.OperationCode != OperationCode.RESERVED))
                .Select(a => a.RegistrationNumber!)
                .ToList()
                .Where(r => !known.Contains(r))
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList();
            this.JournalWriteLine($"ADSBdb で {targets.Count} 機を引く（{AdsbdbInterval.TotalSeconds:0}秒おき）");

            bool first = true;
            int done = 0;
            foreach (string reg in targets)
            {
                if (++done % 25 == 0)
                {
                    string line = $"ADSBdb {done}/{targets.Count} 機目（引けた {result.FromAdsbdb.Count}・無かった {result.Unknown.Count}）";
                    this.JournalWriteLine(line);
                    JobActivity.Report(typeof(RefreshAircraftHexJob), line);
                }
                if (!first)
                {
                    await Task.Delay(AdsbdbInterval, ct);
                }
                first = false;
                string? hex;
                try
                {
                    hex = await AdsbdbClient.FindHexAsync(_http, reg, ct);
                }
                catch (HttpRequestException ex)
                {
                    //429 や 5xx が続くようなら、残りは次回に回す
                    this.JournalWriteLine($"{reg}:ADSBdb でエラーのため中断（{ex.StatusCode}）");
                    result.AdsbdbError = $"{reg} で {(int?)ex.StatusCode} {ex.Message}";
                    break;
                }
                if (hex == null)
                {
                    result.Unknown.Add(reg);
                    continue;
                }
                context.AircraftHexes.Add(new AircraftHex
                {
                    RegistrationNumber = reg,
                    IcaoHex = hex,
                    Source = AircraftHex.SOURCE_ADSBDB,
                    UpdatedAt = DateTime.Now,
                });
                result.FromAdsbdb.Add($"{reg}={hex}");
            }
            await context.SaveChangesAsync(ct);
        }

        /// <summary>
        /// ダンプで見えた (hex, レジ) を今の対応に反映する。existing は追跡中のエンティティで、変更はその場で書き換える。
        /// JAFleet に無いレジ、同じ日に別々の hex で出てきたレジは無視する。
        /// </summary>
        public static HexLearnResult Learn(IDictionary<string, AircraftHex> existing, IEnumerable<HexObservation> observed,
            ISet<string> registrations, DateOnly dataDate, DateTime now)
        {
            var result = new HexLearnResult();
            foreach (var group in observed.GroupBy(o => o.Registration, StringComparer.Ordinal))
            {
                string reg = group.Key;
                if (!registrations.Contains(reg))
                {
                    result.NotInJafleet++;
                    continue;
                }
                var hexes = group.Select(o => o.Hex).Distinct(StringComparer.Ordinal).ToList();
                if (hexes.Count > 1)
                {
                    result.Ambiguous.Add($"{reg}={string.Join("/", hexes)}");
                    continue;
                }
                string hex = hexes[0];
                if (existing.TryGetValue(reg, out var current))
                {
                    if (current.IcaoHex != hex)
                    {
                        result.Changed.Add($"{reg}:{current.IcaoHex}→{hex}");
                        current.IcaoHex = hex;
                    }
                    current.Source = AircraftHex.SOURCE_ADSBLOL;
                    current.LastSeenDate = dataDate;
                    current.UpdatedAt = now;
                    result.Seen++;
                }
                else
                {
                    var added = new AircraftHex
                    {
                        RegistrationNumber = reg,
                        IcaoHex = hex,
                        Source = AircraftHex.SOURCE_ADSBLOL,
                        LastSeenDate = dataDate,
                        UpdatedAt = now,
                    };
                    existing[reg] = added;
                    result.Added.Add(added);
                    result.Seen++;
                }
            }
            return result;
        }
    }

    /// <summary>ダンプで見えた日本の機体1機分（hex とレジ）</summary>
    public record HexObservation(string Hex, string Registration);

    /// <summary>ダンプから覚えた結果</summary>
    public class HexLearnResult
    {
        /// <summary>JAFleet の機体と結び付いた数（新規＋既存）</summary>
        public int Seen { get; set; }
        public List<AircraftHex> Added { get; } = [];
        /// <summary>hex が変わった機体（"JA123A:old→new"）</summary>
        public List<string> Changed { get; } = [];
        /// <summary>同じ日に複数の hex で出てきたので見送った機体</summary>
        public List<string> Ambiguous { get; } = [];
        /// <summary>レジは付いていたが JAFleet に無かった数</summary>
        public int NotInJafleet { get; set; }
    }

    /// <summary>1回分の実行結果</summary>
    public class HexRefreshResult
    {
        public List<string> FromAdsbdb { get; } = [];
        /// <summary>ADSBdb でも見つからなかった機体</summary>
        public List<string> Unknown { get; } = [];
        public string? AdsbdbError { get; set; }

        public string ToSlackMessage()
        {
            var lines = new List<string>
            {
                "hex の補充（ADSBdb）",
                $"引けた {FromAdsbdb.Count} 機、見つからなかった {Unknown.Count} 機",
            };
            if (FromAdsbdb.Count > 0)
            {
                lines.Add("引けた機体: " + string.Join(", ", FromAdsbdb));
            }
            if (AdsbdbError != null)
            {
                lines.Add($"ADSBdb はエラーで途中まで: {AdsbdbError}");
            }
            return string.Join("\n", lines);
        }
    }
}
