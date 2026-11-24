using JAFleet.Commons.Data;

namespace JAFleet.Adsb
{
    /// <summary>出発地・到着地をどうやって決めたか</summary>
    public enum AirportSource
    {
        /// <summary>決められなかった</summary>
        None,
        /// <summary>路線の空港が、軌跡の端の近くにあった</summary>
        Route,
        /// <summary>軌跡の端が高い所で切れていて確かめられないので、路線の空港をそのまま使った</summary>
        RouteUnverified,
        /// <summary>軌跡の端に一番近い空港</summary>
        Nearest,
    }

    public record ResolvedAirport(Airport? Airport, AirportSource Source);

    /// <summary>
    /// フライトの出発地・到着地を決める。
    /// ① callsign の路線（VRS）の空港が軌跡の端の近くにあればそれ
    /// ② 端が高い所で切れていて確かめられなければ、路線の空港をそのまま
    /// ③ 路線が無い・合わないときは、端が低ければ一番近い空港
    /// </summary>
    public class AirportResolver
    {
        /// <summary>地上・低空の端から、路線の空港までこれ以内なら合っているとみる</summary>
        public const double ROUTE_NEAR_KM = 30;
        /// <summary>降下中・上昇中（10,000ft 以下）で切れた端なら、路線の空港までこれ以内で合っているとみる</summary>
        public const double ROUTE_NEAR_LOW_KM = 150;
        /// <summary>一番近い空港で代わりにするときの距離の上限（地上の端）</summary>
        public const double NEAREST_GROUND_KM = 15;
        /// <summary>同（低空の端）</summary>
        public const double NEAREST_LOW_KM = 40;
        /// <summary>この高度（ft）以下で切れた端を「低い」とみる</summary>
        public const int LOW_ALTITUDE = 10000;

        private readonly IReadOnlyDictionary<string, Airport> _byCode;
        private readonly IReadOnlyDictionary<string, string[]> _routes;
        /// <summary>一番近い空港を探す対象（IATA コードのある空港だけ。ヘリポートや私設飛行場を拾わないため）</summary>
        private readonly Airport[] _nearestCandidates;

        public AirportResolver(IEnumerable<Airport> airports, IEnumerable<FlightRoute> routes)
        {
            var list = airports.ToList();
            _byCode = list.ToDictionary(a => a.Code, StringComparer.Ordinal);
            _routes = routes.ToDictionary(r => r.Callsign,
                r => r.AirportCodes.Split('-', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            _nearestCandidates = list.Where(a => a.Iata != null).ToArray();
        }

        public Airport? Find(string code) => _byCode.GetValueOrDefault(code);

        public (ResolvedAirport From, ResolvedAirport To) Resolve(FlightLeg leg)
        {
            ResolvedAirport? from = null, to = null;
            if (CallsignConverter.ToRouteCallsign(leg.Callsign) is string cs && _routes.TryGetValue(cs, out var codes))
            {
                var route = codes.Select(c => _byCode.GetValueOrDefault(c)).ToArray();
                (from, to) = MatchRoute(route, leg);
            }
            from ??= Nearest(leg.Start, isStart: true);
            to ??= Nearest(leg.End, isStart: false);
            return (from, to);
        }

        /// <summary>
        /// 路線の隣り合う2空港の組のうち、軌跡の両端に一番よく合うものを選ぶ（経由便は区間ごとに見る）。
        /// 片側ずつ確かめ、合わない側は null（呼び出し側で一番近い空港にする）。
        /// </summary>
        private static (ResolvedAirport? From, ResolvedAirport? To) MatchRoute(Airport?[] route, FlightLeg leg)
        {
            ResolvedAirport? bestFrom = null, bestTo = null;
            double bestScore = double.MaxValue;
            for (int i = 0; i + 1 < route.Length; i++)
            {
                var f = Check(route[i], leg.Start, isStart: true);
                var t = Check(route[i + 1], leg.End, isStart: false);
                //合った側の距離の和が小さい組ほどよい。合わない側は大きな罰点
                double score = f.Score + t.Score;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestFrom = f.Result;
                    bestTo = t.Result;
                }
            }
            return (bestFrom, bestTo);
        }

        private static (ResolvedAirport? Result, double Score) Check(Airport? airport, TracePoint end, bool isStart)
        {
            const double MISS = 100000;
            if (airport == null)
            {
                return (null, MISS);
            }
            double km = Geo.DistanceKm(end.Latitude, end.Longitude, airport.Latitude, airport.Longitude);
            if (!IsLow(end))
            {
                //巡航中に切れた端は確かめようがない。路線どおりとする
                return (new ResolvedAirport(airport, AirportSource.RouteUnverified), 1000);
            }
            if (!IsHeadingRightWay(end, isStart))
            {
                return (null, MISS);
            }
            double limit = end.OnGround || end.Altitude is <= 3000 ? ROUTE_NEAR_KM : ROUTE_NEAR_LOW_KM;
            return km <= limit ? (new ResolvedAirport(airport, AirportSource.Route), km) : (null, MISS);
        }

        private ResolvedAirport Nearest(TracePoint end, bool isStart)
        {
            if (!IsLow(end) || !IsHeadingRightWay(end, isStart))
            {
                return new ResolvedAirport(null, AirportSource.None);
            }
            double limit = end.OnGround ? NEAREST_GROUND_KM : NEAREST_LOW_KM;
            Airport? best = null;
            double bestKm = double.MaxValue;
            foreach (var a in _nearestCandidates)
            {
                //緯度で先に粗くふるう（1度≒111km）
                if (Math.Abs(a.Latitude - end.Latitude) > 1)
                {
                    continue;
                }
                double km = Geo.DistanceKm(end.Latitude, end.Longitude, a.Latitude, a.Longitude);
                if (km < bestKm)
                {
                    bestKm = km;
                    best = a;
                }
            }
            return best != null && bestKm <= limit
                ? new ResolvedAirport(best, AirportSource.Nearest)
                : new ResolvedAirport(null, AirportSource.None);
        }

        /// <summary>
        /// 空中で切れた端が、出発側なら上昇中（降下中ではない）、到着側なら降下中（上昇中ではない）か。
        /// 空港の近くを降りていく途中から受信が始まった便で、その空港を出発地にしてしまわないため。
        /// 地上の端と、昇降率が分からない端は確かめない
        /// </summary>
        private static bool IsHeadingRightWay(TracePoint p, bool isStart)
        {
            if (p.OnGround || p.VerticalRate is not int v)
            {
                return true;
            }
            return isStart ? v > -VERTICAL_RATE_LEVEL : v < VERTICAL_RATE_LEVEL;
        }

        /// <summary>この昇降率（ft/分）より小さければ水平飛行とみる</summary>
        public const int VERTICAL_RATE_LEVEL = 300;

        private static bool IsLow(TracePoint p) => p.OnGround || p.Altitude is int a && a <= LOW_ALTITUDE;
    }

    public static class Geo
    {
        /// <summary>2点間の大円距離（km）</summary>
        public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            double dLat = ToRad(lat2 - lat1);
            double dLon = ToRad(lon2 - lon1);
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(h)));
        }

        private static double ToRad(double deg) => deg * Math.PI / 180;
    }
}
