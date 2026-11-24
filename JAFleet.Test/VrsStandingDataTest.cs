using System.Formats.Tar;
using System.Text;
using JAFleet.Batch;
using JAFleet.StandingData;

namespace JAFleet.Test
{
    [TestClass]
    public sealed class VrsStandingDataTest
    {
        private static readonly DateTime Now = new(2026, 11, 24, 3, 0, 0);

        [TestMethod]
        public void CSVの引用符とBOMを読める()
        {
            const string csv = "﻿Code,Name,Location\r\n0GA2,\"Airnautique, Inc. Airport\",\"Say \"\"hi\"\"\"\r\n\r\nX,Y,Z";
            var rows = Csv.ReadRecords(csv).ToList();

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("0GA2", rows[0].Get("Code"));
            Assert.AreEqual("Airnautique, Inc. Airport", rows[0].Get("Name"));
            Assert.AreEqual("Say \"hi\"", rows[0].Get("Location"));
            Assert.AreEqual("", rows[1].Get("NoSuchColumn"));
        }

        [TestMethod]
        public void 空港を読み_座標の無い行は捨てる()
        {
            const string csv = """
                Code,Name,ICAO,IATA,Location,CountryISO2,Latitude,Longitude,AltitudeFeet
                RJAA,Narita International Airport,RJAA,NRT,Tokyo,JP,35.764702,140.386002,141
                RJ01,Akasaka Press Center Heliport,RJ01,,Tokyo,JP,35.663333,139.725556,
                XXXX,Broken,XXXX,,,,,,
                """;
            var airports = VrsStandingData.ParseAirports(csv, Now).ToList();

            Assert.AreEqual(2, airports.Count);
            Assert.AreEqual("NRT", airports[0].Iata);
            Assert.AreEqual(35.764702, airports[0].Latitude, 1e-9);
            Assert.AreEqual(141, airports[0].AltitudeFeet);
            Assert.IsNull(airports[1].Iata);
            Assert.IsNull(airports[1].AltitudeFeet);
        }

        [TestMethod]
        public void 路線を読み_空港が2つ未満の行は捨てる()
        {
            const string csv = """
                Callsign,Code,Number,AirlineCode,AirportCodes
                JAL201,JAL,201,JAL,RJTT-RJFF
                JAL3087,JAL,3087,JAL,RJTT-RJFF-ROAH
                JAL9,JAL,9,JAL,RJTT
                """;
            var routes = VrsStandingData.ParseRoutes(csv, Now).ToList();

            Assert.AreEqual(2, routes.Count);
            Assert.AreEqual("JAL201", routes[0].Callsign);
            Assert.AreEqual("201", routes[0].FlightNumber);
            Assert.AreEqual("RJTT-RJFF-ROAH", routes[1].AirportCodes);
        }

        [TestMethod]
        public async Task tarから空港と日本の会社の路線だけを拾う()
        {
            using var tar = new MemoryStream();
            using (var w = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
            {
                Add(w, "standing-data-main/airports/schema-01/R/RJ.csv",
                    "Code,Name,ICAO,IATA,Location,CountryISO2,Latitude,Longitude,AltitudeFeet\nRJTT,Tokyo Haneda,RJTT,HND,Tokyo,JP,35.55,139.78,35\n");
                Add(w, "standing-data-main/routes/schema-01/J/JAL-all.csv",
                    "Callsign,Code,Number,AirlineCode,AirportCodes\nJAL201,JAL,201,JAL,RJTT-RJFF\n");
                Add(w, "standing-data-main/routes/schema-01/B/BAW-all.csv",
                    "Callsign,Code,Number,AirlineCode,AirportCodes\nBAW5,BAW,5,BAW,EGLL-RJTT\n");
                Add(w, "standing-data-main/airlines/schema-01/airlines.csv", "Code,Name\nJAL,Japan Airlines\n");
            }
            tar.Position = 0;

            var result = await VrsStandingData.ReadAsync(tar, Now);

            Assert.AreEqual("RJTT", result.Airports.Single().Code);
            Assert.AreEqual("JAL201", result.Routes.Single().Callsign);
        }

        [TestMethod]
        public void 件数が少なすぎれば入れ替えない()
        {
            Assert.IsNull(RefreshFlightRoute.Check(34128, 6000));
            Assert.IsNotNull(RefreshFlightRoute.Check(100, 6000));
            Assert.IsNotNull(RefreshFlightRoute.Check(34128, 0));
        }

        private static void Add(TarWriter w, string name, string text)
            => w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) });
    }
}
