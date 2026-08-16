using System.Text.Json;
using System.Text.Json.Serialization;

namespace jafleet.Classes.JcabImport
{
    /// <summary>
    /// プレビュー画面で人が触った内容。自動判定の結果に上書きする形で保持する。
    /// 判定ロジックを直したときに、保存済みの作業が古い判定に固定されないようにするため
    /// 判定結果そのものではなく差分だけを持つ。
    /// </summary>
    public class JcabRowOverride
    {
        public bool Selected { get; set; }

        public string? Airline { get; set; }

        public int? TypeDetailId { get; set; }

        public string? OperationCode { get; set; }

        public string? RegisterDate { get; set; }

        public string? SerialNumber { get; set; }

        public string? Remarks { get; set; }

        public bool NotUpdateDate { get; set; }

        /// <summary>セクションを手で移動した場合の行き先</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ImportCategory? Category { get; set; }

        /// <summary>この保存の中で取込済みになったか</summary>
        public bool Imported { get; set; }
    }

    public static class JcabRowOverrideSerializer
    {
        private static readonly JsonSerializerOptions OPTIONS = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string Serialize(Dictionary<string, JcabRowOverride> overrides)
            => JsonSerializer.Serialize(overrides, OPTIONS);

        public static Dictionary<string, JcabRowOverride> Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, JcabRowOverride>>(json, OPTIONS) ?? [];
            }
            catch (JsonException)
            {
                //保存形式が変わった等で読めない場合は、自動判定だけでやり直せるよう空で返す
                return [];
            }
        }
    }
}
