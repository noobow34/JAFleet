using JAFleet.Adsb;
using JAFleet.Commons.Data;
using Microsoft.EntityFrameworkCore;
using Noobow.Commons.Extensions;

namespace JAFleet.Batch
{
    /// <summary>
    /// adsb.lol の履歴ダンプ（GitHub）1日分を流して、日本の機体のフライトを adsb_flight に入れる。
    /// 同じ日を流し直したら、その日の行は入れ替える。ついでにダンプのレジで hex（aircraft_hex）も覚える。
    /// 稼働状況（working_status）にはまだ触らない（試運転のあと切り替える）。
    /// </summary>
    public class RefreshAdsbFlight(DbContextOptions<JAFleetContext> options, HttpClient http)
    {
        public static bool Processing { get; private set; }

        public async Task<AdsbFlightResult> ExecuteAsync(DateOnly dataDate, CancellationToken ct = default)
        {
            Processing = true;
            try
            {
                var result = new AdsbFlightResult { DataDate = dataDate };
                var parts = await AdsbDump.FindPartUrlsAsync(http, dataDate, ct);
                if (parts == null)
                {
                    result.DumpMissing = true;
                    return result;
                }
                var now = DateTime.Now;
                var collector = await CreateCollectorAsync(dataDate, now, ct);
                result.JapanTraceCount = await AdsbDump.ReadJapanTracesAsync(http, parts, (hex, content) =>
                {
                    collector.Add(TraceFile.Parse(hex, content));
                    return Task.CompletedTask;
                }, ct);
                await SaveAsync(collector, result, dataDate, now, ct);
                return result;
            }
            finally
            {
                Processing = false;
            }
        }

        /// <summary>手元に抜き出した軌跡ファイル（hex.json.gz）から流す。試運転・検証用</summary>
        public async Task<AdsbFlightResult> ExecuteFromFilesAsync(DateOnly dataDate, IEnumerable<string> files, CancellationToken ct = default)
        {
            var result = new AdsbFlightResult { DataDate = dataDate };
            var now = DateTime.Now;
            var collector = await CreateCollectorAsync(dataDate, now, ct);
            foreach (string f in files)
            {
                string hex = Path.GetFileName(f).Split('.')[0];
                collector.Add(TraceFile.Parse(hex, await File.ReadAllBytesAsync(f, ct)));
                result.JapanTraceCount++;
            }
            await SaveAsync(collector, result, dataDate, now, ct);
            return result;
        }

        private async Task<FlightCollector> CreateCollectorAsync(DateOnly dataDate, DateTime now, CancellationToken ct)
        {
            using var context = new JAFleetContext(options);
            var registrations = (await context.Aircrafts.AsNoTracking().Select(a => a.RegistrationNumber!).ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);
            var hexToReg = (await context.AircraftHexes.AsNoTracking().ToListAsync(ct))
                .GroupBy(h => h.IcaoHex, StringComparer.Ordinal)
                .Where(g => g.Count() == 1) //同じ hex を2機が持っていたら決められないので使わない
                .ToDictionary(g => g.Key, g => g.Single().RegistrationNumber, StringComparer.Ordinal);
            var resolver = new AirportResolver(
                await context.Airports.AsNoTracking().ToListAsync(ct),
                await context.FlightRoutes.AsNoTracking().ToListAsync(ct));
            return new FlightCollector(registrations, hexToReg, new DailyFlightAnalyzer(resolver), dataDate, now);
        }

        private async Task SaveAsync(FlightCollector collector, AdsbFlightResult result, DateOnly dataDate, DateTime now, CancellationToken ct)
        {
            using var context = new JAFleetContext(options);
            using var tx = await context.Database.BeginTransactionAsync(ct);

            var existing = await context.AircraftHexes.ToDictionaryAsync(h => h.RegistrationNumber, StringComparer.Ordinal, ct);
            var learned = RefreshAircraftHex.Learn(existing, collector.Observations, collector.Registrations, dataDate, now);
            context.AircraftHexes.AddRange(learned.Added);
            result.Learned = learned;

            await context.AdsbFlights.Where(f => f.DataDate == dataDate).ExecuteDeleteAsync(ct);
            context.AdsbFlights.AddRange(collector.Rows);
            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            result.AircraftCount = collector.Rows.Select(r => r.RegistrationNumber).Distinct().Count();
            result.SeenAircraftCount = collector.SeenRegistrations.Count;
            result.FlightCount = collector.Rows.Count;
            result.MatchedByHex = collector.MatchedByHex;
            result.Unmatched = collector.Unmatched;
            this.JournalWriteLine(result.ToSummary());
        }

        /// <summary>軌跡を1本ずつ受け取り、機体に結び付けてフライトの行にする（軌跡そのものは持ち続けない）</summary>
        public class FlightCollector(ISet<string> registrations, IReadOnlyDictionary<string, string> hexToReg,
            DailyFlightAnalyzer analyzer, DateOnly dataDate, DateTime now)
        {
            public ISet<string> Registrations { get; } = registrations;
            public List<AdsbFlight> Rows { get; } = [];
            public List<HexObservation> Observations { get; } = [];
            /// <summary>軌跡があった（飛んでいなくても地上で見えた）機体</summary>
            public HashSet<string> SeenRegistrations { get; } = new(StringComparer.Ordinal);
            public int MatchedByHex { get; private set; }
            /// <summary>JAFleet のどの機体にも結び付かなかった軌跡の数</summary>
            public int Unmatched { get; private set; }

            public void Add(TraceFile trace)
            {
                if (trace.Registration != null)
                {
                    Observations.Add(new HexObservation(trace.Hex, trace.Registration));
                }
                string? reg = trace.Registration != null && Registrations.Contains(trace.Registration)
                    ? trace.Registration
                    : null;
                if (reg == null && hexToReg.TryGetValue(trace.Hex, out var byHex))
                {
                    reg = byHex;
                    MatchedByHex++;
                }
                if (reg == null)
                {
                    Unmatched++;
                    return;
                }
                if (!SeenRegistrations.Add(reg))
                {
                    return; //同じ機体の軌跡が2本（レジと hex の両方で当たった）なら先のものだけ
                }
                Rows.AddRange(analyzer.Analyze(reg, trace, dataDate, now));
            }
        }
    }

    public class AdsbFlightResult
    {
        public DateOnly DataDate { get; set; }
        public bool DumpMissing { get; set; }
        public int JapanTraceCount { get; set; }
        public int SeenAircraftCount { get; set; }
        public int AircraftCount { get; set; }
        public int FlightCount { get; set; }
        public int MatchedByHex { get; set; }
        public int Unmatched { get; set; }
        public HexLearnResult? Learned { get; set; }

        public string ToSummary()
        {
            if (DumpMissing)
            {
                return $"adsb.lol のダンプ（{AdsbDump.TagName(DataDate)}）がまだ公開されていません。";
            }
            return $"adsb.lol のダンプ（{DataDate:yyyy-MM-dd} UTC）: 日本の機体の軌跡 {JapanTraceCount} 件、" +
                $"JAFleet の機体 {SeenAircraftCount} 機（hex で当てた {MatchedByHex}）、うち飛んだ {AircraftCount} 機・{FlightCount} 便。" +
                $"結び付かなかった軌跡 {Unmatched} 件。hex の新規 {Learned?.Added.Count ?? 0}・変更 {Learned?.Changed.Count ?? 0}";
        }
    }
}
