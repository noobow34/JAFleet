using System.Text.RegularExpressions;

namespace JAFleet.Adsb
{
    /// <summary>
    /// ADS-B の callsign（ICAO の航空会社3文字＋番号。例 JAL201）を、FR24 と同じ便名（IATA 2文字＋番号。例 JL201）にする。
    /// 対応表に無い会社や、レジをそのまま callsign にしている飛行（フェリー・試験飛行など）は callsign のまま返す。
    /// </summary>
    public static partial class CallsignConverter
    {
        /// <summary>
        /// callsign の航空会社 → 便名の2文字。VRS の airlines.csv は日本の会社に誤りがあるので自前で持つ。
        /// J-AIR などは親会社の2文字にする。トキエアは FR24 では CG と出ていたが、正しい BV にする。
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> ICAO_TO_IATA = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["JAL"] = "JL",
            ["JLJ"] = "JL",
            ["JAC"] = "JL",
            ["NTH"] = "JL",
            ["RAC"] = "JL",
            ["JTA"] = "NU",
            ["ANA"] = "NH",
            ["AKX"] = "EH", //FR24 の表示に合わせる（ANA ウイングスの自社 callsign の便）
            ["AJX"] = "NQ",
            ["APJ"] = "MM",
            ["JJP"] = "GK",
            ["SKY"] = "BC",
            ["ADO"] = "HD",
            ["SNJ"] = "6J",
            ["SFJ"] = "7G",
            ["IBX"] = "FW",
            ["FDA"] = "JH",
            ["AHX"] = "MZ",
            ["ORC"] = "OC",
            ["NCA"] = "KZ",
            ["TZP"] = "ZG",
            ["TOK"] = "BV",
            ["SJO"] = "IJ",
        };

        /// <summary>
        /// IATA の形で callsign を出す機体（JAC の "JL3724" など）の路線を引くため、2文字 → 路線ファイルの会社。
        /// 2文字を複数の会社が使うもの（JL は JAL・J-AIR・JAC…）は、路線を持っている親会社にする
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> IATA_TO_ROUTE_ICAO = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["JL"] = "JAL", ["NH"] = "ANA", ["NU"] = "JTA", ["NQ"] = "AJX", ["MM"] = "APJ", ["GK"] = "JJP",
            ["BC"] = "SKY", ["HD"] = "ADO", ["6J"] = "SNJ", ["7G"] = "SFJ", ["FW"] = "IBX", ["JH"] = "FDA",
            ["MZ"] = "AHX", ["OC"] = "ORC", ["KZ"] = "NCA", ["ZG"] = "TZP", ["BV"] = "TOK", ["IJ"] = "SJO",
        };

        /// <summary>路線を引くときの callsign。IATA の形（JL3724）なら ICAO の形（JAL3724）にする。それ以外はそのまま</summary>
        public static string? ToRouteCallsign(string? callsign)
        {
            if (callsign == null)
            {
                return null;
            }
            var m = IataCallsign().Match(callsign);
            return m.Success && IATA_TO_ROUTE_ICAO.TryGetValue(m.Groups[1].Value, out string? icao)
                ? icao + m.Groups[2].Value
                : callsign;
        }

        [GeneratedRegex(@"^([A-Z0-9]{2})(\d{1,4}[A-Z]?)$")]
        private static partial Regex IataCallsign();

        public static string? ToFlightNumber(string? callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign))
            {
                return null;
            }
            string cs = callsign.Trim().ToUpperInvariant();
            var m = AirlineCallsign().Match(cs);
            if (!m.Success || !ICAO_TO_IATA.TryGetValue(m.Groups[1].Value, out string? iata))
            {
                return cs;
            }
            string number = m.Groups[2].Value.TrimStart('0');
            return iata + (number.Length == 0 ? "0" : number);
        }

        [GeneratedRegex(@"^([A-Z]{3})(\d{1,4}[A-Z]?)$")]
        private static partial Regex AirlineCallsign();
    }
}
