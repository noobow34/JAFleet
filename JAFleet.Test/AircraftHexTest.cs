using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using JAFleet.Adsb;
using JAFleet.Batch;
using JAFleet.Commons.Data;

namespace JAFleet.Test
{
    [TestClass]
    public sealed class AircraftHexTest
    {
        private static readonly DateOnly DataDate = new(2026, 11, 22);
        private static readonly DateTime Now = new(2026, 11, 23, 13, 0, 0);

        [TestMethod]
        public void タグとURLはデータ日から組み立てる()
        {
            Assert.AreEqual("v2026.11.22-planes-readsb-prod-0", AdsbDump.TagName(DataDate));
            Assert.AreEqual("https://api.github.com/repos/adsblol/globe_history_2026/releases/tags/v2026.11.22-planes-readsb-prod-0",
                AdsbDump.ReleaseApiUrl(DataDate));
        }

        [TestMethod]
        public void リリースからは分割tarだけを名前順に拾う()
        {
            const string json = """
                {"assets":[
                  {"name":"v2026.11.22-planes-readsb-prod-0.tar.ab","browser_download_url":"https://example/ab"},
                  {"name":"other.txt","browser_download_url":"https://example/other"},
                  {"name":"v2026.11.22-planes-readsb-prod-0.tar.aa","browser_download_url":"https://example/aa"}
                ]}
                """;
            var urls = AdsbDump.ParsePartUrls(json, AdsbDump.TagName(DataDate));

            CollectionAssert.AreEqual(new[] { "https://example/aa", "https://example/ab" }, urls!.ToArray());
            Assert.IsNull(AdsbDump.ParsePartUrls("""{"assets":[]}""", AdsbDump.TagName(DataDate)));
        }

        [TestMethod]
        public void リリースの分割tarは大きさも読む_進み具合は人が読める形で出す()
        {
            const string json = """
                {"assets":[
                  {"name":"v2026.11.22-planes-readsb-prod-0.tar.ab","browser_download_url":"https://example/ab","size":1281858048},
                  {"name":"v2026.11.22-planes-readsb-prod-0.tar.aa","browser_download_url":"https://example/aa","size":2000000000}
                ]}
                """;
            var parts = AdsbDump.ParseParts(json, AdsbDump.TagName(DataDate))!;
            Assert.AreEqual(3281858048, parts.Sum(p => p.Size));
            Assert.AreEqual("https://example/aa", parts[0].Url);

            var progress = new DumpProgress(1610612736, 3281858048, 1, 2, 312, TimeSpan.FromSeconds(150));
            Assert.AreEqual("取得中 1.50 / 3.06 GiB（49%）、分割 1/2、日本の機体 312 件、経過 2分30秒", progress.ToString());
        }

        [TestMethod]
        public async Task 読んでいる途中で進み具合を知らせる()
        {
            using var tar = new MemoryStream();
            using (var writer = new TarWriter(tar, TarEntryFormat.Gnu, leaveOpen: true))
            {
                for (int i = 0; i < 5; i++)
                {
                    AddEntry(writer, $"./traces/{i:x2}/trace_full_86{i:x4}.json", Gzip("""{"trace":[]}"""));
                }
            }
            tar.Position = 0;
            var counts = new List<int>();

            await AdsbDump.ReadJapanTracesAsync(tar, (_, _) => Task.CompletedTask, onCount: counts.Add, progressInterval: TimeSpan.Zero);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, counts, "間隔 0 ならエントリごとに、それまでの件数を知らせる");
        }

        [TestMethod]
        public void 日本のhexの軌跡ファイルだけを拾う()
        {
            Assert.AreEqual("86dcbc", AdsbDump.JapanHexFromEntryName("./traces/bc/trace_full_86DCBC.json"));
            Assert.AreEqual("840000", AdsbDump.JapanHexFromEntryName("traces/00/trace_full_840000.json"));
            Assert.AreEqual("87ffff", AdsbDump.JapanHexFromEntryName("traces/ff/trace_full_87ffff.json"));
            Assert.IsNull(AdsbDump.JapanHexFromEntryName("traces/ff/trace_full_880000.json"));
            Assert.IsNull(AdsbDump.JapanHexFromEntryName("traces/ff/trace_full_83ffff.json"));
            Assert.IsNull(AdsbDump.JapanHexFromEntryName("traces/bc/trace_full_~86dcbc.json"));
            Assert.IsNull(AdsbDump.JapanHexFromEntryName("traces/bc/trace_86dcbc.json"));
        }

        [TestMethod]
        public async Task tarを流して日本の機体のレジを読める()
        {
            using var tar = new MemoryStream();
            using (var writer = new TarWriter(tar, TarEntryFormat.Gnu, leaveOpen: true))
            {
                AddEntry(writer, "./traces/bc/trace_full_86dcbc.json", Gzip("""{"icao":"86dcbc","r":"ja841a ","t":"DH8D","trace":[]}"""));
                AddEntry(writer, "./traces/01/trace_full_a00001.json", Gzip("""{"icao":"a00001","r":"N1","trace":[]}"""));
                AddEntry(writer, "./traces/02/trace_full_850002.json", Gzip("""{"icao":"850002","trace":[]}"""));
            }
            tar.Position = 0;

            var read = new List<TraceFile>();
            int count = await AdsbDump.ReadJapanTracesAsync(tar, (hex, content) =>
            {
                read.Add(TraceFile.Parse(hex, content));
                return Task.CompletedTask;
            });

            Assert.AreEqual(2, count);
            Assert.AreEqual(("86dcbc", "JA841A", "DH8D"), (read[0].Hex, read[0].Registration, read[0].TypeCode));
            Assert.AreEqual(("850002", (string?)null, (string?)null), (read[1].Hex, read[1].Registration, read[1].TypeCode));
        }

        [TestMethod]
        public async Task 途中で分割されたtarをつないで読める()
        {
            using var tar = new MemoryStream();
            using (var writer = new TarWriter(tar, TarEntryFormat.Gnu, leaveOpen: true))
            {
                for (int i = 0; i < 50; i++)
                {
                    AddEntry(writer, $"./traces/{i:x2}/trace_full_86{i:x4}.json", Gzip($$"""{"r":"JA{{i:D3}}A","trace":[]}"""));
                    AddEntry(writer, $"./traces/{i:x2}/trace_full_a0{i:x4}.json", new byte[3000]);
                }
            }
            byte[] all = tar.ToArray();
            //ブロックの境目でもエントリの境目でもない所で3つに切る
            int[] cuts = [0, 12345, 77777, all.Length];
            var parts = Enumerable.Range(0, 3).Select(i => all[cuts[i]..cuts[i + 1]]).ToArray();
            using var joined = new AdsbDump.ConcatenatedStream((index, _) =>
                Task.FromResult<Stream?>(index < parts.Length ? new MemoryStream(parts[index]) : null));

            var regs = new List<string?>();
            int count = await AdsbDump.ReadJapanTracesAsync(joined, (hex, content) =>
            {
                regs.Add(TraceFile.Parse(hex, content).Registration);
                return Task.CompletedTask;
            });

            Assert.AreEqual(50, count);
            Assert.AreEqual("JA000A", regs[0]);
            Assert.AreEqual("JA049A", regs[49]);
        }

        [TestMethod]
        public void 新しい機体は足し_既存は最終確認日を進め_hexが変われば書き換える()
        {
            var existing = new Dictionary<string, AircraftHex>
            {
                ["JA841A"] = Hex("JA841A", "86dcbc", AircraftHex.SOURCE_ADSBDB, null),
                ["JA312J"] = Hex("JA312J", "111111", AircraftHex.SOURCE_ADSBLOL, new DateOnly(2026, 11, 1)),
            };
            var observed = new[]
            {
                new HexObservation("86dcbc", "JA841A"),
                new HexObservation("85aaaa", "JA312J"),
                new HexObservation("861111", "JA737Y"),
                new HexObservation("862222", "JA999X"), //JAFleet に無い
            };
            var regs = new HashSet<string> { "JA841A", "JA312J", "JA737Y" };

            var result = RefreshAircraftHex.Learn(existing, observed, regs, DataDate, Now);

            Assert.AreEqual(3, result.Seen);
            Assert.AreEqual(1, result.NotInJafleet);
            Assert.AreEqual("JA737Y", result.Added.Single().RegistrationNumber);
            CollectionAssert.AreEqual(new[] { "JA312J:111111→85aaaa" }, result.Changed);
            Assert.AreEqual(AircraftHex.SOURCE_ADSBLOL, existing["JA841A"].Source);
            Assert.AreEqual(DataDate, existing["JA841A"].LastSeenDate);
            Assert.AreEqual("85aaaa", existing["JA312J"].IcaoHex);
        }

        [TestMethod]
        public void 同じ日に複数のhexで出たレジは見送る()
        {
            var existing = new Dictionary<string, AircraftHex>();
            var observed = new[] { new HexObservation("861111", "JA737Y"), new HexObservation("862222", "JA737Y") };

            var result = RefreshAircraftHex.Learn(existing, observed, new HashSet<string> { "JA737Y" }, DataDate, Now);

            Assert.AreEqual(0, result.Seen);
            Assert.AreEqual(0, existing.Count);
            CollectionAssert.AreEqual(new[] { "JA737Y=861111/862222" }, result.Ambiguous);
        }

        [TestMethod]
        public void ADSBdbの応答からhexを抜く()
        {
            const string found = """{"response":{"aircraft":{"mode_s":"86DCBC","registration":"JA841A"}}}""";
            Assert.AreEqual("86dcbc", AdsbdbClient.ParseHex(found, "JA841A"));
            Assert.IsNull(AdsbdbClient.ParseHex(found, "JA842A"), "レジが違う");
            Assert.IsNull(AdsbdbClient.ParseHex("""{"response":"unknown aircraft"}""", "JA841A"));
            Assert.IsNull(AdsbdbClient.ParseHex("""{"response":{"aircraft":{"mode_s":"A00001","registration":"JA841A"}}}""", "JA841A"), "日本の範囲外");
        }

        private static AircraftHex Hex(string reg, string hex, string source, DateOnly? lastSeen)
            => new() { RegistrationNumber = reg, IcaoHex = hex, Source = source, LastSeenDate = lastSeen, UpdatedAt = Now.AddDays(-30) };

        private static byte[] Gzip(string json)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
            {
                gz.Write(Encoding.UTF8.GetBytes(json));
            }
            return ms.ToArray();
        }

        private static void AddEntry(TarWriter writer, string name, byte[] content)
        {
            var entry = new GnuTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(content) };
            writer.WriteEntry(entry);
        }
    }
}
