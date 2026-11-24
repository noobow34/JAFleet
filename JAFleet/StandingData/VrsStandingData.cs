using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using JAFleet.Adsb;
using JAFleet.Commons.Data;

namespace JAFleet.StandingData
{
    /// <summary>
    /// VRS standing-data（https://github.com/vradarserver/standing-data、CC0）の路線と空港を読む。
    /// リポジトリの tarball（約6MB）を1回取って流し、必要な CSV だけを拾う。
    /// </summary>
    public static partial class VrsStandingData
    {
        public const string TARBALL_URL = "https://codeload.github.com/vradarserver/standing-data/tar.gz/refs/heads/main";

        /// <summary>
        /// 路線を取り込む callsign の航空会社（ICAO 3文字）。JA 機が使うものだけ（25社）。
        /// HAC（NTH）・J-AIR の一部など、自社の callsign を使わない会社は JAL・ANA のファイルに入っている。
        /// VRS に無い会社（JAC・NTH・CUK など）は 0 件のまま。
        /// </summary>
        public static readonly IReadOnlySet<string> JAPANESE_CALLSIGN_AIRLINES = new HashSet<string>(StringComparer.Ordinal)
        {
            "JAL", "ANA", "APJ", "JJP", "SKY", "ADO", "SNJ", "SFJ", "JTA", "RAC", "IBX", "FDA",
            "JAC", "NTH", "CUK", "AHX", "ORC", "AKX", "JLJ", "NCA", "TZP", "AJX", "TOK", "WAJ", "SJO",
        };

        /// <summary>読んだ結果</summary>
        public record Result(List<Airport> Airports, List<FlightRoute> Routes);

        public static async Task<Result> DownloadAsync(HttpClient http, DateTime now, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, TARBALL_URL);
            req.Headers.UserAgent.ParseAdd(AdsbDump.USER_AGENT);
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            res.EnsureSuccessStatusCode();
            using var body = await res.Content.ReadAsStreamAsync(ct);
            using var gz = new GZipStream(body, CompressionMode.Decompress);
            return await ReadAsync(gz, now, ct);
        }

        /// <summary>展開済み（gzip を外した）tar から読む。テストではこちらを直接使う</summary>
        public static async Task<Result> ReadAsync(Stream tarStream, DateTime now, CancellationToken ct = default)
        {
            var airports = new Dictionary<string, Airport>(StringComparer.Ordinal);
            var routes = new Dictionary<string, FlightRoute>(StringComparer.Ordinal);
            using var reader = new TarReader(tarStream, leaveOpen: true);
            TarEntry? entry;
            while ((entry = await reader.GetNextEntryAsync(copyData: false, ct)) != null)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream == null)
                {
                    continue;
                }
                if (AirportFile().IsMatch(entry.Name))
                {
                    foreach (var a in ParseAirports(await ReadTextAsync(entry.DataStream, ct), now))
                    {
                        airports[a.Code] = a;
                    }
                }
                else if (RouteFile().Match(entry.Name) is { Success: true } m && JAPANESE_CALLSIGN_AIRLINES.Contains(m.Groups[1].Value))
                {
                    foreach (var r in ParseRoutes(await ReadTextAsync(entry.DataStream, ct), now))
                    {
                        routes[r.Callsign] = r;
                    }
                }
            }
            return new Result([.. airports.Values], [.. routes.Values]);
        }

        [GeneratedRegex(@"/airports/schema-01/[^/]+/[^/]+\.csv$")]
        private static partial Regex AirportFile();

        [GeneratedRegex(@"/routes/schema-01/[^/]+/([A-Z0-9]{2,4})-all\.csv$")]
        private static partial Regex RouteFile();

        private static async Task<string> ReadTextAsync(Stream s, CancellationToken ct)
        {
            using var sr = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            return await sr.ReadToEndAsync(ct);
        }

        /// <summary>Code,Name,ICAO,IATA,Location,CountryISO2,Latitude,Longitude,AltitudeFeet。座標の無い行は捨てる</summary>
        public static IEnumerable<Airport> ParseAirports(string csv, DateTime now)
        {
            foreach (var row in Csv.ReadRecords(csv))
            {
                string code = row.Get("Code");
                if (code.Length == 0
                    || !double.TryParse(row.Get("Latitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
                    || !double.TryParse(row.Get("Longitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                {
                    continue;
                }
                yield return new Airport
                {
                    Code = code,
                    Name = row.Get("Name"),
                    Icao = NullIfEmpty(row.Get("ICAO")),
                    Iata = NullIfEmpty(row.Get("IATA")),
                    Location = NullIfEmpty(row.Get("Location")),
                    CountryIso2 = NullIfEmpty(row.Get("CountryISO2")),
                    Latitude = lat,
                    Longitude = lon,
                    AltitudeFeet = int.TryParse(row.Get("AltitudeFeet"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int alt) ? alt : null,
                    UpdatedAt = now,
                };
            }
        }

        /// <summary>Callsign,Code,Number,AirlineCode,AirportCodes。空港が2つ未満の行は捨てる</summary>
        public static IEnumerable<FlightRoute> ParseRoutes(string csv, DateTime now)
        {
            foreach (var row in Csv.ReadRecords(csv))
            {
                string callsign = row.Get("Callsign").ToUpperInvariant();
                string airports = row.Get("AirportCodes").ToUpperInvariant();
                if (callsign.Length == 0 || airports.Split('-', StringSplitOptions.RemoveEmptyEntries).Length < 2)
                {
                    continue;
                }
                yield return new FlightRoute
                {
                    Callsign = callsign,
                    AirlineIcao = row.Get("Code").ToUpperInvariant(),
                    FlightNumber = row.Get("Number"),
                    AirportCodes = airports,
                    UpdatedAt = now,
                };
            }
        }

        private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
    }

    /// <summary>
    /// VRS の CSV（RFC 4180 寄り。値は二重引用符で囲んでもよく、中の二重引用符は2つ重ねる）を読む最小の実装
    /// </summary>
    public static class Csv
    {
        public sealed class Record(Dictionary<string, int> header, List<string> values)
        {
            public string Get(string column)
                => header.TryGetValue(column, out int i) && i < values.Count ? values[i].Trim() : "";
        }

        public static IEnumerable<Record> ReadRecords(string text)
        {
            using var rows = ReadRows(text.TrimStart('﻿')).GetEnumerator();
            if (!rows.MoveNext())
            {
                yield break;
            }
            var header = rows.Current
                .Select((name, i) => (name: name.Trim(), i))
                .GroupBy(h => h.name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().i, StringComparer.Ordinal);
            while (rows.MoveNext())
            {
                if (rows.Current.Count == 1 && rows.Current[0].Length == 0)
                {
                    continue; //空行
                }
                yield return new Record(header, rows.Current);
            }
        }

        public static IEnumerable<List<string>> ReadRows(string text)
        {
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    row.Add(field.ToString());
                    field.Clear();
                    yield return row;
                    row = [];
                }
                else
                {
                    field.Append(c);
                }
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                yield return row;
            }
        }
    }
}
