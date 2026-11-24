using AngleSharp.Html.Parser;
using JAFleet.Commons.Scraping;

namespace JAFleet.Test
{
    /// <summary>
    /// Flightradar24の機体ページ（Inertia.jsのdata-page JSON）からの抽出テスト。
    /// 実際のページのJSONから、使う項目だけを抜き出した形にしてある
    /// </summary>
    [TestClass]
    public class FlightradarExtractorTest
    {
        private const long DEP_ESTIMATED = 1795165466;  //2026-11-20 18:04:26 JST
        private const long ARR_ESTIMATED = 1795177644;  //2026-11-20 21:27:24 JST

        private static string BuildPage(string propsJson)
        {
            //data-page属性にはHTMLエスケープされたJSONが入っている
            string escaped = propsJson.Replace("\"", "&quot;");
            return $"<html><body><div id=\"app\" data-page=\"{escaped}\">描画済みのHTML</div></body></html>";
        }

        private static AngleSharp.Dom.IDocument Parse(string html)
            => new HtmlParser().ParseDocument(html);

        private const string AircraftPageJson = """
            {"component":"Data/AircraftRegistration","props":{
              "aircraft":{"registration":"JA841A","model":"De Havilland Canada Dash 8-400","typeCode":"DH8D"},
              "photos":[
                {"url":"https://cdn.jetphotos.com/640/5/2004473_1787809680.jpg?v=0","link":"https://www.jetphotos.com/photo/12213190","copyright":"Luca Radulescu"}
              ],
              "flights":[
                {"flightNumber":"NH1237","callsign":null,"activeDay":"2026-11-20",
                 "scheduledDeparture":1795171200,"actualDeparture":null,"estimatedDeparture":1795165466,
                 "scheduledArrival":1795176900,"estimatedArrival":1795177644,
                 "status":{"name":"landed","color":"green","board":"arrivals"},
                 "origin":{"name":"Komatsu Airport","city":"Komatsu","iata":"KMQ","icao":"RJNK"},
                 "destination":{"name":"Fukuoka Airport","city":"Fukuoka","iata":"FUK","icao":"RJFF"},
                 "divertedTo":null,"live":false},
                {"flightNumber":"NH1238","callsign":null,"activeDay":"2026-11-20",
                 "scheduledDeparture":1795164300,"actualDeparture":null,"estimatedDeparture":null,
                 "status":{"name":"estimated"},
                 "origin":{"name":"Fukuoka Airport","city":"Fukuoka","iata":"FUK"},
                 "destination":{"name":"Komatsu Airport","city":"Komatsu","iata":"KMQ"},
                 "divertedTo":null,"live":false}
              ],
              "historyAvailable":true,"locked":true}}
            """;

        [TestMethod]
        public void フライト履歴と写真を取り出せる()
        {
            var data = AircraftDataExtractor.ExtractAircraftDataFromFlightradar(Parse(BuildPage(AircraftPageJson)));

            Assert.IsTrue(data.IsAircraftPage);
            Assert.AreEqual(2, data.Flights.Count);

            var latest = data.Flights[0];
            //実出発が無ければ出発見込みを使う
            Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(DEP_ESTIMATED).ToOffset(TimeSpan.FromHours(9)).DateTime, latest.DepartureDate);
            Assert.AreEqual("NH1237", latest.FlightNumber);
            Assert.AreEqual("Komatsu (KMQ)", latest.FromAp);
            Assert.AreEqual("Fukuoka (FUK)", latest.ToAp);
            Assert.AreEqual($"Landed {DateTimeOffset.FromUnixTimeSeconds(ARR_ESTIMATED).ToOffset(TimeSpan.FromHours(9)):HH:mm}", latest.Status);

            //到着時刻が無ければ状況だけ
            Assert.AreEqual("Estimated", data.Flights[1].Status);

            Assert.IsNotNull(data.Photo);
            Assert.AreEqual("https://www.jetphotos.com/photo/12213190", data.Photo!.PhotoUrl);
            Assert.AreEqual("https://cdn.jetphotos.com/640/5/2004473_1787809680.jpg", data.Photo.PhotoDirectSmall);
            Assert.AreEqual("https://cdn.jetphotos.com/full/5/2004473_1787809680.jpg", data.Photo.PhotoDirectLarge);
        }

        [TestMethod]
        public void 運航実績が無い機体は空のフライト履歴になる()
        {
            string json = """
                {"component":"Data/AircraftRegistration","props":{
                  "aircraft":{"registration":"JA801A"},
                  "photos":[{"url":"https://cdn.jetphotos.com/640/6/1898645_1792932447.jpg?v=0","link":"https://www.jetphotos.com/photo/12270847"}],
                  "flights":[]}}
                """;

            var data = AircraftDataExtractor.ExtractAircraftDataFromFlightradar(Parse(BuildPage(json)));

            Assert.IsTrue(data.IsAircraftPage);
            Assert.AreEqual(0, data.Flights.Count);
            Assert.IsNotNull(data.Photo);
        }

        [TestMethod]
        public void 存在しないレジは機体ページではないと判定する()
        {
            //未知のレジは機種一覧へリダイレクトされ、機体のpropsを持たない
            string json = """
                {"component":"Data/Aircraft","props":{"families":[],"content":{}}}
                """;

            var data = AircraftDataExtractor.ExtractAircraftDataFromFlightradar(Parse(BuildPage(json)));

            Assert.IsFalse(data.IsAircraftPage);
            Assert.AreEqual(0, data.Flights.Count);
            Assert.IsNull(data.Photo);
        }

        [TestMethod]
        public void 写真が無ければ写真はnullになる()
        {
            string json = """
                {"component":"Data/AircraftRegistration","props":{
                  "aircraft":{"registration":"JA801A"},"photos":[],"flights":[]}}
                """;

            var data = AircraftDataExtractor.ExtractAircraftDataFromFlightradar(Parse(BuildPage(json)));

            Assert.IsNull(data.Photo);
        }

        [TestMethod]
        public void JSONが無いHTMLでも例外にならない()
        {
            var noApp = AircraftDataExtractor.ExtractAircraftDataFromFlightradar(Parse("<html><body><p>Error</p></body></html>"));
            Assert.IsFalse(noApp.IsAircraftPage);
            Assert.AreEqual(0, noApp.Flights.Count);

            var brokenJson = AircraftDataExtractor.ExtractAircraftDataFromFlightradar(
                Parse("<html><body><div id=\"app\" data-page=\"{&quot;props&quot;:\"></div></body></html>"));
            Assert.IsFalse(brokenJson.IsAircraftPage);
            Assert.AreEqual(0, brokenJson.Flights.Count);

            Assert.IsFalse(AircraftDataExtractor.ExtractAircraftDataFromFlightradar(null).IsAircraftPage);
        }
    }
}
