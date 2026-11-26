using JAFleet.Adsb;
using JAFleet.Batch;
using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using JAFleet.Services;

namespace JAFleet.Test
{
    [TestClass]
    public sealed class AdsbWorkingStatusTest
    {
        //データ日 11/22（UTC）→ 締めは 11/23 9:00。11/21 以降に飛んでいれば稼働
        private static readonly DateTime Reference = new(2026, 11, 23);
        private static readonly DateTime Now = new(2026, 11, 23, 13, 5, 0);
        private static readonly Airport Tpe = new() { Code = "RCTP", Iata = "TPE", Name = "Taoyuan", Latitude = 25.0777, Longitude = 121.233002 };

        [TestMethod]
        public void 非稼働の機体が飛んだら稼働になり_前回の日時も残す()
        {
            var a = Aircraft(OperationCode.DOMESTIC);
            var status = new WorkingStatus { RegistrationNumber = "JA841A", Working = false, FlightDate = new DateTime(2026, 11, 10, 8, 0, 0) };
            var sections = new WorkingStatusSections();

            AdsbWorkingStatus.Apply(a, status, Flight(new DateTime(2026, 11, 22, 10, 5, 0), "Tokyo (HND)", "Nagoya (NGO)"), null,
                Reference, [Tpe], sections, Now);

            Assert.IsTrue(status.Working);
            Assert.AreEqual(new DateTime(2026, 11, 22, 10, 5, 0), status.FlightDate);
            Assert.AreEqual("Nagoya (NGO)", status.ToAp);
            Assert.AreEqual("NH1234", status.FlightNumber);
            Assert.AreEqual("2026/11/10 08:00", sections.ToWorking3["JA841A"].PreviousDate);
        }

        [TestMethod]
        public void 二日以内に飛んでいれば今日飛ばなくても稼働のまま()
        {
            var status = new WorkingStatus { RegistrationNumber = "JA841A", Working = true, FlightDate = new DateTime(2026, 11, 21, 20, 0, 0) };
            var sections = new WorkingStatusSections();

            AdsbWorkingStatus.Apply(Aircraft(OperationCode.DOMESTIC), status, null, null, Reference, [Tpe], sections, Now);

            Assert.IsTrue(status.Working);
            Assert.AreEqual(0, sections.ToSources().Sum(s => s.dict.Count));
        }

        [TestMethod]
        public void 整備拠点に着いて非稼働になったら整備入り_また飛んだら整備終了()
        {
            var a = Aircraft(OperationCode.DOMESTIC);
            var status = new WorkingStatus { RegistrationNumber = "JA841A", Working = true, FlightDate = new DateTime(2026, 11, 20, 9, 0, 0), ToAp = "Taipei (TPE)" };
            var last = Flight(new DateTime(2026, 11, 20, 9, 0, 0), "Tokyo (NRT)", "Taipei (TPE)");
            var sections = new WorkingStatusSections();

            AdsbWorkingStatus.Apply(a, status, null, last, Reference, [Tpe], sections, Now);

            Assert.IsFalse(status.Working);
            Assert.IsTrue(status.Maintenancing);
            Assert.IsTrue(sections.ToNotWorking.ContainsKey("JA841A"));
            Assert.IsTrue(sections.MainteStart.ContainsKey("JA841A"));

            //整備中のあいだは毎日「整備中」に出る
            var next = new WorkingStatusSections();
            AdsbWorkingStatus.Apply(a, status, null, last, Reference.AddDays(1), [Tpe], next, Now);
            Assert.IsTrue(next.Mainteing.ContainsKey("JA841A"));

            //戻ってきた
            var back = new WorkingStatusSections();
            AdsbWorkingStatus.Apply(a, status, Flight(new DateTime(2026, 12, 5, 10, 0, 0), "Taipei (TPE)", "Tokyo (NRT)"), null,
                new DateTime(2026, 12, 6), [Tpe], back, Now);
            Assert.IsTrue(status.Working);
            Assert.IsFalse(status.Maintenancing);
            Assert.IsTrue(back.MainteEnd.ContainsKey("JA841A"));
        }

        [TestMethod]
        public void 到着地が決まらなくても最後の位置が拠点の近くなら整備入り()
        {
            var nearTpe = Flight(new DateTime(2026, 11, 20, 9, 0, 0), "Tokyo (NRT)", null);
            nearTpe.ToCode = null;
            nearTpe.EndLat = 25.3;
            nearTpe.EndLon = 121.5;
            nearTpe.EndAltitude = 6000;
            Assert.IsTrue(AdsbWorkingStatus.IsMaintenancePlace(nearTpe, null, [Tpe]));

            nearTpe.EndAltitude = 35000;
            Assert.IsFalse(AdsbWorkingStatus.IsMaintenancePlace(nearTpe, null, [Tpe]), "巡航中に切れたものは拾わない");

            Assert.IsFalse(AdsbWorkingStatus.IsMaintenancePlace(Flight(DateTime.Now, "Tokyo (HND)", "Osaka (ITM)"), null, [Tpe]));
            Assert.IsTrue(AdsbWorkingStatus.IsMaintenancePlace(null, "Manila (MNL)", [Tpe]), "adsb の便が無ければ今の到着地で見る");
        }

        [TestMethod]
        public void 長すぎる文字列は列の長さに切り詰めて入れる()
        {
            var status = new WorkingStatus { RegistrationNumber = "JA841A", Working = true, FlightDate = new DateTime(2026, 11, 20, 8, 0, 0) };
            var flight = Flight(new DateTime(2026, 11, 22, 10, 5, 0), "Tokyo (HND)", "Hagåtña, Guam International Airport (GUM)");

            AdsbWorkingStatus.Apply(Aircraft(OperationCode.INTERNATIONAL), status, flight, null, Reference, [Tpe], new WorkingStatusSections(), Now);

            Assert.AreEqual(30, status.ToAp!.Length);
            Assert.IsTrue(status.ToAp.EndsWith("…"));
            Assert.AreEqual("Tokyo (HND)", status.FromAp);
        }

        [TestMethod]
        public void 古い日を流し直しても新しい便を上書きしない()
        {
            var status = new WorkingStatus { RegistrationNumber = "JA841A", Working = true, FlightDate = new DateTime(2026, 11, 22, 20, 0, 0), ToAp = "Osaka (ITM)" };
            AdsbWorkingStatus.Apply(Aircraft(OperationCode.DOMESTIC), status, Flight(new DateTime(2026, 11, 21, 8, 0, 0), "Tokyo (HND)", "Nagoya (NGO)"),
                null, Reference, [Tpe], new WorkingStatusSections(), Now);

            Assert.AreEqual(new DateTime(2026, 11, 22, 20, 0, 0), status.FlightDate);
            Assert.AreEqual("Osaka (ITM)", status.ToAp);
        }

        [TestMethod]
        public void テストレジは新しく飛んだときだけ知らせる()
        {
            var a = Aircraft(OperationCode.MAKING);
            a.TestRegistration = "N1786B";
            var status = new WorkingStatus { RegistrationNumber = "JA841A", TestFlightDate = new DateTime(2026, 11, 20, 8, 0, 0) };

            var same = new WorkingStatusSections();
            AdsbWorkingStatus.ApplyTestFlight(a, status, new DateTime(2026, 11, 20, 8, 0, 0), same);
            Assert.AreEqual(0, same.ToWorkingTest.Count);

            var newer = new WorkingStatusSections();
            AdsbWorkingStatus.ApplyTestFlight(a, status, new DateTime(2026, 11, 22, 9, 30, 0), newer);
            Assert.AreEqual("test:N1786B", newer.ToWorkingTest["JA841A"].Mark);
            Assert.AreEqual(new DateTime(2026, 11, 22, 9, 30, 0), status.TestFlightDate);
        }

        [TestMethod]
        public void 区分の並びはFR24と同じ()
        {
            CollectionAssert.AreEqual(
                new[] { "テストレジが稼働", "予約登録が稼働", "製造中が稼働", "デリバリーが稼働", "運用中非稼働が稼働", "退役未抹消が稼働", "稼働が非稼働", "整備入り", "整備終了", "整備中" },
                new WorkingStatusSections().ToSources().Select(s => s.title).ToArray());
        }

        [TestMethod]
        public void 取得元は行が無ければadsb_fr24と書いたときだけFR24()
        {
            Assert.AreEqual(WorkingStatusSource.Adsb, SiteSettings.ReadSource(new Dictionary<string, string>()));
            Assert.AreEqual(WorkingStatusSource.Fr24, SiteSettings.ReadSource(new Dictionary<string, string> { [SiteSettings.WORKING_STATUS_SOURCE] = " FR24 " }));
            Assert.AreEqual(WorkingStatusSource.Adsb, SiteSettings.ReadSource(new Dictionary<string, string> { [SiteSettings.WORKING_STATUS_SOURCE] = "なにか" }));
        }

        [TestMethod]
        public void テストレジのhexは日本の範囲の外でも取り出す()
        {
            Assert.IsNull(AdsbDump.JapanHexFromEntryName("traces/6b/trace_full_a1b26b.json"));
            Assert.AreEqual("a1b26b", AdsbDump.HexFromEntryName("traces/6b/trace_full_A1B26B.json"));
            const string json = """{"response":{"aircraft":{"mode_s":"A1B26B","registration":"N1786B"}}}""";
            Assert.IsNull(AdsbdbClient.ParseHex(json, "N1786B"));
            Assert.AreEqual("a1b26b", AdsbdbClient.ParseHex(json, "N1786B", japanOnly: false));
        }

        private static AircraftView Aircraft(string operationCode) => new()
        {
            RegistrationNumber = "JA841A",
            OperationCode = operationCode,
            TypeDetailName = "DHC-8-400",
        };

        private static AdsbFlight Flight(DateTime departure, string? from, string? to) => new()
        {
            RegistrationNumber = "JA841A",
            DataDate = DateOnly.FromDateTime(departure),
            LegNo = 1,
            IcaoHex = "86dcbc",
            Callsign = "ANA1234",
            FlightNumber = "NH1234",
            FromAp = from,
            ToAp = to,
            ToCode = to == null ? null : "XXXX",
            DepartureTime = departure,
            ArrivalTime = departure.AddHours(1),
            Landed = true,
            Status = $"Landed {departure.AddHours(1):HH:mm}",
        };
    }
}
