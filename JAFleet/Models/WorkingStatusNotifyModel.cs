using System.Text.Json.Serialization;

namespace JAFleet.Models
{
    // ============================================================
    // 稼働チェック結果のSlack通知用JSON（LogType.WORKING_NOTIFY_TEXTに格納）
    // ============================================================

    /// <summary>通知対象の1機体（JSON用）</summary>
    public record WorkingStatusNotifyRegJson
    {
        [JsonPropertyName("reg")]  public string  Reg  { get; init; } = string.Empty;
        /// <summary>特別塗装(◎)・整備通知(☆)のマーク</summary>
        [JsonPropertyName("mark")] public string? Mark { get; init; }
    }

    /// <summary>1区分（テストレジが稼働、整備入り、など）のJSON用</summary>
    public record WorkingStatusNotifySectionJson
    {
        [JsonPropertyName("title")] public string                          Title { get; init; } = string.Empty;
        [JsonPropertyName("regs")]  public List<WorkingStatusNotifyRegJson> Regs  { get; init; } = new();
    }

    /// <summary>1バッチ実行分の通知内容（JSON用ルートオブジェクト）</summary>
    public record WorkingStatusNotifyJson
    {
        [JsonPropertyName("finishedAt")]  public string?                             FinishedAt  { get; init; }
        [JsonPropertyName("elapsed")]     public string?                             Elapsed     { get; init; }
        [JsonPropertyName("waitSeconds")] public double                              WaitSeconds { get; init; }
        /// <summary>稼働チェックログ画面へのリンク用（yyyyMMdd）</summary>
        [JsonPropertyName("logDate")]     public string?                             LogDate     { get; init; }
        [JsonPropertyName("sections")]    public List<WorkingStatusNotifySectionJson> Sections   { get; init; } = new();
    }
}
