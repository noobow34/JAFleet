using System.Formats.Tar;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JAFleet.Adsb
{
    /// <summary>
    /// adsb.lol の履歴ダンプ（GitHub の adsblol/globe_history_YYYY のリリース、ODbL 1.0）を読む。
    /// 1日（UTC）分の全機の軌跡が tar を分割した .tar.aa / .tar.ab … で上がるので、
    /// 保存せずにつないで流し、日本の hex（840000〜87FFFF）のファイルだけを取り出す。
    /// adsb.lol のサーバー（地図・API）は自動で叩かない決まりなので、ここでは GitHub だけを相手にする。
    /// </summary>
    public static partial class AdsbDump
    {
        public const string USER_AGENT = "JA-Fleet/1.0 (+https://ja-fleet.noobow.me)";

        /// <summary>日本に割り当てられた ICAO 24bit アドレスの範囲</summary>
        public const int JAPAN_HEX_FROM = 0x840000;
        public const int JAPAN_HEX_TO = 0x87FFFF;

        /// <summary>そのデータ日（UTC）の本番（prod-0）リリースのタグ</summary>
        public static string TagName(DateOnly dataDate)
            => $"v{dataDate.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture)}-planes-readsb-prod-0";

        /// <summary>リリース情報の API の URL。リポジトリは年ごとに分かれている</summary>
        public static string ReleaseApiUrl(DateOnly dataDate)
            => $"https://api.github.com/repos/adsblol/globe_history_{dataDate.Year}/releases/tags/{TagName(dataDate)}";

        /// <summary>
        /// リリースに付いている分割 tar を、つなぐ順（名前順）に返す。リリースがまだ無ければ null。
        /// </summary>
        public static async Task<IReadOnlyList<DumpPart>?> FindPartsAsync(HttpClient http, DateOnly dataDate, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ReleaseApiUrl(dataDate));
            req.Headers.UserAgent.ParseAdd(USER_AGENT);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            res.EnsureSuccessStatusCode();
            string json = await res.Content.ReadAsStringAsync(ct);
            return ParseParts(json, TagName(dataDate));
        }

        /// <summary>リリース情報の JSON から分割 tar（タグ名.tar.xx）の URL を名前順に抜く。無ければ null</summary>
        public static IReadOnlyList<string>? ParsePartUrls(string releaseJson, string tagName)
            => ParseParts(releaseJson, tagName)?.Select(p => p.Url).ToList();

        /// <summary>リリース情報の JSON から分割 tar（タグ名.tar.xx）を名前順に抜く。無ければ null</summary>
        public static IReadOnlyList<DumpPart>? ParseParts(string releaseJson, string tagName)
        {
            using var doc = JsonDocument.Parse(releaseJson);
            if (!doc.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var parts = assets.EnumerateArray()
                .Select(a => new DumpPart(
                    a.GetProperty("name").GetString() ?? "",
                    a.GetProperty("browser_download_url").GetString() ?? "",
                    a.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0))
                .Where(a => a.Name.StartsWith(tagName + ".tar.", StringComparison.Ordinal))
                .OrderBy(a => a.Name, StringComparer.Ordinal)
                .ToList();
            return parts.Count == 0 ? null : parts;
        }

        /// <summary>
        /// tar のエントリ名が日本の機体の軌跡ファイル（traces/xx/trace_full_&lt;hex&gt;.json）なら hex（小文字6桁）を返す。
        /// </summary>
        public static string? JapanHexFromEntryName(string entryName)
        {
            if (HexFromEntryName(entryName) is not string hex)
            {
                return null;
            }
            int value = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return value is >= JAPAN_HEX_FROM and <= JAPAN_HEX_TO ? hex : null;
        }

        /// <summary>軌跡ファイルなら hex（小文字6桁）を返す。範囲は問わない</summary>
        public static string? HexFromEntryName(string entryName)
        {
            var m = TraceFileName().Match(entryName);
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
        }

        [GeneratedRegex(@"(?:^|/)trace_full_([0-9a-fA-F]{6})\.json$")]
        private static partial Regex TraceFileName();

        /// <summary>
        /// 分割 tar をつないで流し、日本の機体の軌跡ファイルごとに onTrace(hex, 中身) を呼ぶ。
        /// 中身は gzip のままのバイト列（TraceFile.Parse に渡す）。返り値は読んだ日本の機体の数。
        /// </summary>
        /// <remarks>onProgress があれば、progressInterval（既定30秒）ごとに取得の進み具合を渡す</remarks>
        public static async Task<int> ReadJapanTracesAsync(HttpClient http, IReadOnlyList<DumpPart> parts,
            Func<string, byte[], Task> onTrace, CancellationToken ct = default, IReadOnlySet<string>? extraHexes = null,
            Action<DumpProgress>? onProgress = null, TimeSpan? progressInterval = null)
        {
            using var stream = new ConcatenatedStream(HttpPartOpener(http, parts.Select(p => p.Url).ToList()));
            long total = parts.Sum(p => p.Size);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Action<int>? onCount = onProgress == null ? null
                : count => onProgress(new DumpProgress(stream.BytesRead, total, Math.Max(stream.PartIndex, 0) + 1, parts.Count, count, sw.Elapsed));
            return await ReadJapanTracesAsync(stream, onTrace, ct, extraHexes, onCount, progressInterval);
        }

        /// <summary>tar のストリームから日本の機体の軌跡ファイルを取り出す（テストではこちらを直接使う）</summary>
        /// <remarks>extraHexes に入れた hex（テストレジの外国の hex など）も、日本の範囲の外でも取り出す</remarks>
        public static async Task<int> ReadJapanTracesAsync(Stream tarStream, Func<string, byte[], Task> onTrace, CancellationToken ct = default,
            IReadOnlySet<string>? extraHexes = null, Action<int>? onCount = null, TimeSpan? progressInterval = null)
        {
            int count = 0;
            var interval = progressInterval ?? TimeSpan.FromSeconds(30);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var nextReport = interval;
            using var reader = new TarReader(tarStream, leaveOpen: true);
            TarEntry? entry;
            while ((entry = await reader.GetNextEntryAsync(copyData: false, ct)) != null)
            {
                if (onCount != null && sw.Elapsed >= nextReport)
                {
                    onCount(count);
                    nextReport = sw.Elapsed + interval;
                }
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream == null)
                {
                    continue;
                }
                string? hex = JapanHexFromEntryName(entry.Name)
                    ?? (extraHexes is { Count: > 0 } && HexFromEntryName(entry.Name) is string any && extraHexes.Contains(any) ? any : null);
                if (hex == null)
                {
                    continue; //読み飛ばせば TarReader が次のエントリまで進める
                }
                using var ms = new MemoryStream();
                await entry.DataStream.CopyToAsync(ms, ct);
                await onTrace(hex, ms.ToArray());
                count++;
            }
            return count;
        }

        /// <summary>分割 tar の n 番目を HTTP で開く。もう無ければ null</summary>
        private static Func<int, CancellationToken, Task<Stream?>> HttpPartOpener(HttpClient http, IReadOnlyList<string> urls)
            => async (index, ct) =>
            {
                if (index >= urls.Count)
                {
                    return null;
                }
                using var req = new HttpRequestMessage(HttpMethod.Get, urls[index]);
                req.Headers.UserAgent.ParseAdd(USER_AGENT);
                var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                res.EnsureSuccessStatusCode();
                //ストリームを閉じれば接続も返る
                return await res.Content.ReadAsStreamAsync(ct);
            };

        /// <summary>
        /// 分割されたファイルを順に開いて1本のストリームとして読ませる（前から読むだけ）。
        /// openPart(n) は n 番目（0 から）を開き、もう無ければ null を返す。
        /// </summary>
        public sealed class ConcatenatedStream(Func<int, CancellationToken, Task<Stream?>> openPart) : Stream
        {
            private int _index = -1;
            private bool _finished;
            private Stream? _current;

            /// <summary>ここまでに読んだバイト数（全分割の合計）</summary>
            public long BytesRead { get; private set; }

            /// <summary>いま読んでいる分割（0 から。まだ開いていなければ -1）</summary>
            public int PartIndex => _index;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
                => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
                => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            {
                //長さ0の読み取りにも下のストリームは0を返すので、それを「この分割の終わり」と取り違えないようにする
                if (buffer.Length == 0 || _finished)
                {
                    return 0;
                }
                while (true)
                {
                    if (_current == null)
                    {
                        _current = await openPart(++_index, ct);
                        if (_current == null)
                        {
                            _finished = true;
                            return 0;
                        }
                    }
                    int n = await _current.ReadAsync(buffer, ct);
                    if (n > 0)
                    {
                        BytesRead += n;
                        return n;
                    }
                    _current.Dispose();
                    _current = null;
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _current?.Dispose();
                    _current = null;
                }
                base.Dispose(disposing);
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    /// <summary>リリースに付いた分割 tar 1つ（名前・URL・バイト数）</summary>
    public record DumpPart(string Name, string Url, long Size);

    /// <summary>ダンプの取得の進み具合</summary>
    public record DumpProgress(long BytesRead, long TotalBytes, int Part, int PartCount, int JapanTraces, TimeSpan Elapsed)
    {
        public override string ToString()
        {
            const double GiB = 1024d * 1024 * 1024;
            string percent = TotalBytes > 0 ? $"（{100.0 * BytesRead / TotalBytes:0}%）" : "";
            return $"取得中 {BytesRead / GiB:0.00} / {TotalBytes / GiB:0.00} GiB{percent}、分割 {Part}/{PartCount}、" +
                $"日本の機体 {JapanTraces} 件、経過 {(int)Elapsed.TotalMinutes}分{Elapsed.Seconds:00}秒";
        }
    }
}
