using JAFleet.Commons.Data;

namespace JAFleet.Adsb
{
    /// <summary>1機・1日分の軌跡を adsb_flight の行にする</summary>
    public class DailyFlightAnalyzer(AirportResolver resolver)
    {
        private static readonly TimeZoneInfo Jst = FindJst();

        public List<AdsbFlight> Analyze(string registrationNumber, TraceFile trace, DateOnly dataDate, DateTime now)
        {
            var rows = new List<AdsbFlight>();
            int no = 0;
            foreach (var leg in FlightLegSplitter.Split(trace.Points))
            {
                var (from, to) = resolver.Resolve(leg);
                var arrival = ToJst(leg.AirborneTo);
                //接地まで受信できない空港が多いので、降下中に到着地の近くで切れたものも着陸とみる
                bool landed = leg.Landed || to.Source is AirportSource.Route or AirportSource.Nearest;
                rows.Add(new AdsbFlight
                {
                    RegistrationNumber = registrationNumber,
                    DataDate = dataDate,
                    LegNo = ++no,
                    IcaoHex = trace.Hex,
                    Callsign = leg.Callsign,
                    FlightNumber = CallsignConverter.ToFlightNumber(leg.Callsign),
                    FromCode = from.Airport?.Code,
                    ToCode = to.Airport?.Code,
                    FromAp = Display(from.Airport),
                    ToAp = Display(to.Airport),
                    FromSource = SourceName(from.Source),
                    ToSource = SourceName(to.Source),
                    DepartureTime = ToJst(leg.AirborneFrom),
                    ArrivalTime = arrival,
                    Landed = landed,
                    Status = landed ? $"Landed {arrival:HH:mm}" : $"Last seen {arrival:HH:mm}",
                    StartLat = leg.Start.Latitude,
                    StartLon = leg.Start.Longitude,
                    EndLat = leg.End.Latitude,
                    EndLon = leg.End.Longitude,
                    EndAltitude = leg.End.Altitude,
                    UpdatedAt = now,
                });
            }
            return rows;
        }

        /// <summary>FR24 と同じ「都市名 (IATA)」。IATA が無ければ ICAO、都市名が無ければ空港名</summary>
        /// <summary>
        /// 表示（「都市名 (IATA)」）の長さの上限。working_status の from_ap / to_ap が varchar(30) のため
        /// </summary>
        public const int DISPLAY_MAX = 30;

        /// <summary>
        /// FR24 と同じ「都市名 (IATA)」。IATA が無ければ ICAO、都市名が無ければ空港名。
        /// VRS の都市名には「Hagåtña, Guam International Airport」のような長いものがあるので、最初の「,」の前だけを使い、
        /// それでも長ければ末尾を「…」にして DISPLAY_MAX 文字に収める
        /// </summary>
        public static string? Display(Airport? airport)
        {
            if (airport == null)
            {
                return null;
            }
            string code = $" ({airport.Iata ?? airport.Icao ?? airport.Code})";
            string city = (airport.Location ?? airport.Name).Split(',')[0].Trim();
            int room = DISPLAY_MAX - code.Length;
            if (city.Length > room)
            {
                city = city[..Math.Max(0, room - 1)].TrimEnd() + "…";
            }
            return city + code;
        }

        public static string SourceName(AirportSource source) => source switch
        {
            AirportSource.Route => "route",
            AirportSource.RouteUnverified => "route_unverified",
            AirportSource.Nearest => "nearest",
            _ => "none",
        };

        public static DateTime ToJst(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, Jst).DateTime;

        private static TimeZoneInfo FindJst()
        {
            foreach (string id in new[] { "Asia/Tokyo", "Tokyo Standard Time" })
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch (TimeZoneNotFoundException)
                {
                }
            }
            return TimeZoneInfo.CreateCustomTimeZone("JST", TimeSpan.FromHours(9), "JST", "JST");
        }
    }
}
