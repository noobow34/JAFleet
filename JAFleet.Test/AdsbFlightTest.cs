using System.Text;
using JAFleet.Adsb;
using JAFleet.Commons.Data;

namespace JAFleet.Test
{
    [TestClass]
    public sealed class AdsbFlightTest
    {
        private static readonly DateTimeOffset T0 = new(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);

        //羽田・中部・福岡の座標（VRS と同じ値）
        private static readonly Airport Hnd = Ap("RJTT", "HND", "Tokyo", 35.552299, 139.779999);
        private static readonly Airport Ngo = Ap("RJGG", "NGO", "Nagoya", 34.858398, 136.804993);
        private static readonly Airport Fuk = Ap("RJFF", "FUK", "Fukuoka", 33.585899, 130.451004);
        private static readonly Airport Heliport = Ap("RJ01", null, "Tokyo", 35.56, 139.78);

        [TestMethod]
        public void 軌跡の点を読み_callsignは次に出るまで続く()
        {
            const string json = """
                {"icao":"86dcbc","r":"JA841A","t":"DH8D","timestamp":1795392000.0,
                 "trace":[
                   [0.0,35.55,139.78,"ground",10,0,0,null,{"flight":"ANA1234 "}],
                   [60.5,35.56,139.79,1200,150,90,2,1500,null],
                   [120,35.6,139.9,null,300,90,0,null,null]
                 ]}
                """;
            var trace = TraceFile.Parse("86DCBC", Encoding.UTF8.GetBytes(json));

            Assert.AreEqual(3, trace.Points.Count);
            Assert.IsTrue(trace.Points[0].OnGround);
            Assert.AreEqual(0, trace.Points[0].Altitude);
            Assert.AreEqual(1200, trace.Points[1].Altitude);
            Assert.IsTrue(trace.Points[1].IsNewLeg);
            Assert.IsNull(trace.Points[2].Altitude);
            Assert.AreEqual("ANA1234", trace.Points[2].Callsign);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1795392060).AddMilliseconds(500), trace.Points[1].Time);
        }

        [TestMethod]
        public void 地上で区切って2便に分け_地上だけの区間は捨てる()
        {
            var points = new List<TracePoint>();
            points.AddRange(Ground(Hnd, 0, 3, "JAL201"));
            points.AddRange(Air(Hnd, Ngo, 5, 60, "JAL201"));
            points.AddRange(Ground(Ngo, 70, 3, "JAL201"));
            //次の便（フラグなし。地上から空中に出たので区切る）
            points.AddRange(Air(Ngo, Hnd, 120, 60, "JAL202"));
            points.AddRange(Ground(Hnd, 190, 3, "JAL202"));
            points.AddRange(Ground(Hnd, 600, 5, "JAL202")); //夜の駐機

            var legs = FlightLegSplitter.Split(points);

            Assert.AreEqual(2, legs.Count);
            Assert.AreEqual("JAL201", legs[0].Callsign);
            Assert.AreEqual("JAL202", legs[1].Callsign);
            Assert.IsTrue(legs.All(l => l.Landed));
            Assert.AreEqual(T0.AddMinutes(5), legs[0].AirborneFrom);
        }

        [TestMethod]
        public void 低い高度のまま長く途切れたら区切り_高い所の途切れは区切らない()
        {
            var points = new List<TracePoint>();
            points.AddRange(Air(Hnd, Ngo, 0, 30, "JAL201", endAltitude: 1500));
            points.AddRange(Air(Ngo, Hnd, 120, 30, "JAL202", startAltitude: 1500));
            var legs = FlightLegSplitter.Split(points);
            Assert.AreEqual(2, legs.Count, "地方空港で地上が見えなくても、低空の途切れで区切る");

            //巡航中に2時間途切れた（洋上など）は1便のまま
            var cruise = new List<TracePoint>
            {
                P(0, 35.5, 139.7, 30000, "JAL1"), P(1, 35.6, 140.0, 35000, "JAL1"), P(2, 35.7, 140.3, 35000, "JAL1"),
                P(130, 45.0, 170.0, 35000, "JAL1"), P(131, 45.1, 170.3, 35000, "JAL1"),
            };
            Assert.AreEqual(1, FlightLegSplitter.Split(cruise).Count);
        }

        [TestMethod]
        public void 路線の空港が端の近くにあれば路線どおり()
        {
            var resolver = Resolver(("JAL201", "RJTT-RJGG"));
            var leg = Leg("JAL201", Hnd, Ngo);

            var (from, to) = resolver.Resolve(leg);

            Assert.AreEqual(("RJTT", AirportSource.Route), (from.Airport?.Code, from.Source));
            Assert.AreEqual(("RJGG", AirportSource.Route), (to.Airport?.Code, to.Source));
        }

        [TestMethod]
        public void 路線と違う所に降りたら一番近い空港にする()
        {
            //路線は羽田→中部だが、実際は福岡に降りた（ダイバート・路線の古いデータ）
            var resolver = Resolver(("JAL201", "RJTT-RJGG"));
            var (from, to) = resolver.Resolve(Leg("JAL201", Hnd, Fuk));

            Assert.AreEqual(AirportSource.Route, from.Source);
            Assert.AreEqual(("RJFF", AirportSource.Nearest), (to.Airport?.Code, to.Source));
        }

        [TestMethod]
        public void 経由便は軌跡に合う区間を選ぶ()
        {
            var resolver = Resolver(("JAL3087", "RJTT-RJGG-RJFF"));
            var (from, to) = resolver.Resolve(Leg("JAL3087", Ngo, Fuk));

            Assert.AreEqual("RJGG", from.Airport?.Code);
            Assert.AreEqual("RJFF", to.Airport?.Code);
        }

        [TestMethod]
        public void 巡航中に切れた端は路線どおり_路線も無ければ決めない()
        {
            var resolver = Resolver(("JAL201", "RJTT-RJGG"));
            var high = new TracePoint(T0, 35.0, 138.0, false, 35000, 0, 0, "JAL201");
            var leg = new FlightLeg("JAL201", Pt(Hnd, true), high, T0, T0, false);

            var (_, to) = resolver.Resolve(leg);
            Assert.AreEqual(("RJGG", AirportSource.RouteUnverified), (to.Airport?.Code, to.Source));

            var (_, none) = resolver.Resolve(leg with { Callsign = "JA841A" });
            Assert.AreEqual((null, AirportSource.None), (none.Airport?.Code, none.Source));
        }

        [TestMethod]
        public void 一番近い空港はIATAのある空港から選ぶ()
        {
            var resolver = Resolver();
            var (from, _) = resolver.Resolve(Leg(null, Heliport, Ngo));
            Assert.AreEqual("RJTT", from.Airport?.Code, "ヘリポートより羽田");
        }

        [TestMethod]
        public void callsignが別の便に変われば区切る()
        {
            //巡航中の途切れをまたいで次の便が始まった（高い所どうしなので途切れでは区切れない）
            var points = new List<TracePoint>
            {
                P(0, 33.5, 130.4, 20000, "FDA144"), P(1, 33.6, 131.0, 30000, "FDA144"), P(2, 33.7, 132.0, 30000, "FDA144"),
                P(200, 34.0, 136.0, 30000, "FDA312"), P(201, 34.1, 136.2, 30000, "FDA312"), P(202, 34.2, 136.4, 20000, "FDA312"),
            };
            var legs = FlightLegSplitter.Split(points);
            CollectionAssert.AreEqual(new[] { "FDA144", "FDA312" }, legs.Select(l => l.Callsign).ToArray());
        }

        [TestMethod]
        public void 降りていく途中から受信が始まった便は_その空港を出発地にしない()
        {
            var resolver = Resolver();
            var descendingNearHnd = new TracePoint(T0, Hnd.Latitude + 0.1, Hnd.Longitude, false, 3000, -800, 0, null);
            var leg = new FlightLeg(null, descendingNearHnd, Pt(Hnd, true), T0, T0.AddMinutes(5), true);

            var (from, to) = resolver.Resolve(leg);

            Assert.AreEqual(AirportSource.None, from.Source);
            Assert.AreEqual("RJTT", to.Airport?.Code);
        }

        [TestMethod]
        public void IATAの形のcallsignでも路線を引く()
        {
            var resolver = Resolver(("JAL3724", "RJTT-RJGG"));
            var (from, _) = resolver.Resolve(Leg("JL3724", Hnd, Ngo));
            Assert.AreEqual(AirportSource.Route, from.Source);
            Assert.AreEqual("JAL3724", CallsignConverter.ToRouteCallsign("JL3724"));
            Assert.AreEqual("JA841A", CallsignConverter.ToRouteCallsign("JA841A"));
            Assert.IsNull(TraceFile.NormalizeCallsign("@@@@@@@@"));
            Assert.AreEqual("JAL1", TraceFile.NormalizeCallsign(" jal1 "));
        }

        [TestMethod]
        public void callsignを便名にする()
        {
            Assert.AreEqual("JL201", CallsignConverter.ToFlightNumber("JAL201"));
            Assert.AreEqual("NH1", CallsignConverter.ToFlightNumber("ANA001 "));
            Assert.AreEqual("EH5552", CallsignConverter.ToFlightNumber("AKX5552"));
            Assert.AreEqual("6J52", CallsignConverter.ToFlightNumber("SNJ52"));
            Assert.AreEqual("JA841A", CallsignConverter.ToFlightNumber("JA841A"), "レジの callsign はそのまま");
            Assert.AreEqual("BAW5", CallsignConverter.ToFlightNumber("BAW5"), "表に無い会社はそのまま");
            Assert.IsNull(CallsignConverter.ToFlightNumber(" "));
        }

        [TestMethod]
        public void 表示はFR24と同じ都市名とIATA()
        {
            Assert.AreEqual("Tokyo (HND)", DailyFlightAnalyzer.Display(Hnd));
            Assert.AreEqual("Tokyo (RJ01)", DailyFlightAnalyzer.Display(Heliport));
            Assert.AreEqual(new DateTime(2026, 11, 22, 9, 0, 0), DailyFlightAnalyzer.ToJst(T0));
        }

        private static AirportResolver Resolver(params (string Callsign, string Codes)[] routes)
            => new([Hnd, Ngo, Fuk, Heliport], routes.Select(r => new FlightRoute
            {
                Callsign = r.Callsign, AirlineIcao = r.Callsign[..3], FlightNumber = r.Callsign[3..], AirportCodes = r.Codes,
            }));

        private static FlightLeg Leg(string? callsign, Airport from, Airport to)
            => new(callsign, Pt(from, true), Pt(to, true), T0, T0.AddHours(1), true);

        private static TracePoint Pt(Airport a, bool ground) => new(T0, a.Latitude, a.Longitude, ground, ground ? 0 : 1000, 0, 0, null);

        private static TracePoint P(double minutes, double lat, double lon, int alt, string cs)
            => new(T0.AddMinutes(minutes), lat, lon, false, alt, 0, 0, cs);

        private static IEnumerable<TracePoint> Ground(Airport a, double startMinutes, int count, string cs)
            => Enumerable.Range(0, count).Select(i => new TracePoint(T0.AddMinutes(startMinutes + i), a.Latitude, a.Longitude, true, 0, 0, 0, cs));

        /// <summary>from から to へまっすぐ飛ぶ点（両端は低く、真ん中は高い）</summary>
        private static IEnumerable<TracePoint> Air(Airport from, Airport to, double startMinutes, int minutes, string cs,
            int startAltitude = 500, int endAltitude = 500)
        {
            for (int i = 0; i <= 10; i++)
            {
                double r = i / 10.0;
                int alt = i == 0 ? startAltitude : i == 10 ? endAltitude : 30000;
                yield return new TracePoint(T0.AddMinutes(startMinutes + minutes * r),
                    from.Latitude + (to.Latitude - from.Latitude) * r, from.Longitude + (to.Longitude - from.Longitude) * r,
                    false, alt, 0, 0, cs);
            }
        }

        private static Airport Ap(string code, string? iata, string city, double lat, double lon)
            => new() { Code = code, Icao = code, Iata = iata, Name = city + " Airport", Location = city, Latitude = lat, Longitude = lon };
    }
}
