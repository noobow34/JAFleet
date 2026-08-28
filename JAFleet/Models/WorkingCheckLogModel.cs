using System.Text.Json.Serialization;

namespace JAFleet.Models
{
    // ============================================================
    // JSON保存用レコード（LogDetail に格納される構造）
    // ============================================================

    /// <summary>1機体分のログエントリ（JSON用）</summary>
    public record WorkingCheckLogEntryJson
    {
        [JsonPropertyName("reg")]          public string? Reg          { get; init; }
        [JsonPropertyName("mark")]         public string? Mark         { get; init; }
        [JsonPropertyName("type")]         public string? Type         { get; init; }
        [JsonPropertyName("date")]         public string? Date         { get; init; }
        [JsonPropertyName("fromAp")]       public string? FromAp       { get; init; }
        [JsonPropertyName("toAp")]         public string? ToAp         { get; init; }
        [JsonPropertyName("flightNumber")] public string? FlightNumber { get; init; }
        [JsonPropertyName("status")]       public string? Status       { get; init; }
        [JsonPropertyName("previousDate")] public string? PreviousDate { get; init; }
    }

    /// <summary>1セクション（--------見出し--------）のJSON用</summary>
    public record WorkingCheckLogSectionJson
    {
        [JsonPropertyName("title")]   public string?                        Title   { get; init; }
        [JsonPropertyName("entries")] public List<WorkingCheckLogEntryJson> Entries { get; init; } = new();
    }

    /// <summary>1バッチ実行分のJSON用ルートオブジェクト</summary>
    public record WorkingCheckLogJson
    {
        [JsonPropertyName("finishedAt")] public string?                          FinishedAt { get; init; }
        [JsonPropertyName("sections")]   public List<WorkingCheckLogSectionJson> Sections   { get; init; } = new();
    }

    // ============================================================
    // View用ViewModel
    // ============================================================

    /// <summary>1機体分のViewモデル</summary>
    public class WorkingCheckLogEntry
    {
        public string? RegistrationNumber { get; set; }
        public string? NotifyMark         { get; set; }
        public string? TypeName           { get; set; }
        public string? FlightDate         { get; set; }
        public string? FromAp             { get; set; }
        public string? FromApCode         { get; set; }
        public string? FromApName         { get; set; }
        public string? ToAp               { get; set; }
        public string? ToApCode           { get; set; }
        public string? ToApName           { get; set; }
        public string? FlightNumber       { get; set; }
        public string? Status             { get; set; }
        public string? PreviousDate       { get; set; }
        /// <summary>JSONパース失敗時のフォールバック用</summary>
        public string? RawLine            { get; set; }
    }

    public class WorkingCheckLogSection
    {
        public string?                    SectionTitle { get; set; }
        public List<WorkingCheckLogEntry> Entries      { get; set; } = new();
    }

    public class WorkingCheckLogBatch
    {
        public string?                       FinishedAt { get; set; }
        public List<WorkingCheckLogSection>  Sections   { get; set; } = new();
        public IEnumerable<WorkingCheckLogEntry> AllEntries => Sections.SelectMany(s => s.Entries);
    }

    public class WorkingCheckLogModel : BaseModel
    {
        public DateTime                  SearchDate { get; set; }
        public List<WorkingCheckLogBatch> Batches   { get; set; } = new();
        public int TotalEntryCount => Batches.Sum(b => b.AllEntries.Count());
    }
}
