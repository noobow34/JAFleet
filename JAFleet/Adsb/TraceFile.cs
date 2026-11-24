using System.IO.Compression;
using System.Text.Json;

namespace JAFleet.Adsb
{
    /// <summary>
    /// 履歴ダンプの軌跡ファイル（trace_full_&lt;hex&gt;.json、拡張子は json だが中身は gzip）の見出し部分。
    /// いまは機体の突き合わせに使う項目だけを読む。軌跡（trace）の解析は稼働判定を作り替えるときに足す。
    /// </summary>
    public record TraceFile(string Hex, string? Registration, string? TypeCode)
    {
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
                GetString(root, "t"));
        }

        /// <summary>レジを JAFleet の形（大文字・前後の空白なし）にそろえる。空なら null</summary>
        public static string? NormalizeRegistration(string? r)
        {
            string? s = r?.Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        private static string? GetString(JsonElement root, string name)
            => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static bool IsGzip(byte[] content) => content.Length >= 2 && content[0] == 0x1f && content[1] == 0x8b;
    }
}
