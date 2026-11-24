using System.Net;
using System.Text.Json;

namespace JAFleet.Adsb
{
    /// <summary>
    /// ADSBdb（https://www.adsbdb.com/）の機体情報で、レジから ICAO 24bit アドレスを引く。
    /// 履歴ダンプにレジ付きで出てこなかった機体（新造機など）を補うためだけに使う。
    /// ADSBdb の路線データは転載・DB化が禁止なので使わない。
    /// </summary>
    public static class AdsbdbClient
    {
        public static string AircraftUrl(string registrationNumber)
            => $"https://api.adsbdb.com/v0/aircraft/{Uri.EscapeDataString(registrationNumber)}";

        /// <summary>見つかれば小文字6桁の hex、登録が無ければ null。通信エラーは例外のまま投げる</summary>
        public static async Task<string?> FindHexAsync(HttpClient http, string registrationNumber, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, AircraftUrl(registrationNumber));
            req.Headers.UserAgent.ParseAdd(AdsbDump.USER_AGENT);
            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            res.EnsureSuccessStatusCode();
            return ParseHex(await res.Content.ReadAsStringAsync(ct), registrationNumber);
        }

        /// <summary>
        /// 応答の JSON から hex を抜く。レジが一致しないもの、日本の範囲（840000〜87FFFF）の外のものは捨てる。
        /// </summary>
        public static string? ParseHex(string json, string registrationNumber)
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var response)
                || response.ValueKind != JsonValueKind.Object
                || !response.TryGetProperty("aircraft", out var aircraft)
                || aircraft.ValueKind != JsonValueKind.Object)
            {
                return null; //{"response":"unknown aircraft"}
            }
            string? reg = aircraft.TryGetProperty("registration", out var r) ? TraceFile.NormalizeRegistration(r.GetString()) : null;
            string? hex = aircraft.TryGetProperty("mode_s", out var m) ? m.GetString()?.Trim().ToLowerInvariant() : null;
            if (reg != TraceFile.NormalizeRegistration(registrationNumber) || hex is not { Length: 6 })
            {
                return null;
            }
            if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int value)
                || value is < AdsbDump.JAPAN_HEX_FROM or > AdsbDump.JAPAN_HEX_TO)
            {
                return null;
            }
            return hex;
        }
    }
}
