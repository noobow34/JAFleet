using System.IO.Compression;
using System.Text.Json;

namespace JAFleet.Adsb
{
    /// <summary>
    /// 履歴ダンプの軌跡ファイル（trace_full_&lt;hex&gt;.json、拡張子は json だが中身は gzip）。
    /// 形式は readsb の trace_full。trace の各点は
    /// [基準時刻からの秒, 緯度, 経度, 高度ft または "ground", 対地速度, 針路, フラグ, 昇降率, 機体情報 or null, …]。
    /// 機体情報（callsign は flight）は変わったときだけ入るので、次に入るまで前の値が続く。
    /// フラグは 1=位置が古い、2=新しい区間（着陸と離陸の切れ目）の始まり。
    /// </summary>
    public record TraceFile(string Hex, string? Registration, string? TypeCode, IReadOnlyList<TracePoint> Points)
    {
        public const int FLAG_STALE = 1;
        public const int FLAG_NEW_LEG = 2;

        /// <summary>gzip のままのバイト列を読む。gzip でなければそのまま JSON として読む</summary>
        public static TraceFile Parse(string hex, byte[] content)
        {
            using Stream json = IsGzip(content)
                ? new GZipStream(new MemoryStream(content), CompressionMode.Decompress)
                : new MemoryStream(content);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new TraceFile(
                hex.ToLowerInvariant(),
                NormalizeRegistration(GetString(root, "r")),
                GetString(root, "t"),
                ParsePoints(root));
        }

        private static List<TracePoint> ParsePoints(JsonElement root)
        {
            var points = new List<TracePoint>();
            if (!root.TryGetProperty("trace", out var trace) || trace.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("timestamp", out var ts) || ts.ValueKind != JsonValueKind.Number)
            {
                return points;
            }
            double baseTime = ts.GetDouble();
            string? callsign = null;
            foreach (var p in trace.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() < 7)
                {
                    continue;
                }
                double? lat = Number(p[1]);
                double? lon = Number(p[2]);
                if (lat == null || lon == null)
                {
                    continue;
                }
                bool ground = p[3].ValueKind == JsonValueKind.String && p[3].GetString() == "ground";
                int? alt = ground ? 0 : (Number(p[3]) is double a ? (int)a : null);
                int flags = Number(p[6]) is double f ? (int)f : 0;
                int? vrate = p.GetArrayLength() > 7 && Number(p[7]) is double v ? (int)v : null;
                if (p.GetArrayLength() > 8 && p[8].ValueKind == JsonValueKind.Object
                    && p[8].TryGetProperty("flight", out var flight) && flight.ValueKind == JsonValueKind.String)
                {
                    callsign = NormalizeCallsign(flight.GetString());
                }
                points.Add(new TracePoint(
                    DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round((baseTime + p[0].GetDouble()) * 1000)),
                    lat.Value, lon.Value, ground, alt, vrate, flags, callsign));
            }
            return points;
        }

        /// <summary>callsign を大文字・空白なしにする。空や受信のゴミ（"@@@@@@@@" など英数字以外を含むもの）は null</summary>
        public static string? NormalizeCallsign(string? raw)
        {
            string? cs = raw?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(cs) || !cs.All(char.IsAsciiLetterOrDigit))
            {
                return null;
            }
            return cs;
        }

        /// <summary>レジを JAFleet の形（大文字・前後の空白なし）にそろえる。空なら null</summary>
        public static string? NormalizeRegistration(string? r)
        {
            string? s = r?.Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        private static double? Number(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;

        private static string? GetString(JsonElement root, string name)
            => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static bool IsGzip(byte[] content) => content.Length >= 2 && content[0] == 0x1f && content[1] == 0x8b;
    }

    /// <summary>軌跡の1点。Altitude は ft（地上なら 0、不明なら null）、VerticalRate は ft/分</summary>
    public record TracePoint(DateTimeOffset Time, double Latitude, double Longitude, bool OnGround, int? Altitude,
        int? VerticalRate, int Flags, string? Callsign)
    {
        public bool IsNewLeg => (Flags & TraceFile.FLAG_NEW_LEG) != 0;
    }
}
