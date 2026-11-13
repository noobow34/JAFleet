namespace JAFleet.Models
{
    /// <summary>特別塗装ニュースの承認画面（/LiveryNews）</summary>
    public class LiveryNewsModel : BaseModel
    {
        /// <summary>未処理の候補。これが画面の主役</summary>
        public List<LiveryCandidateRow> Pending { get; set; } = [];

        /// <summary>承認・却下・差分なしになった候補（直近分）</summary>
        public List<LiveryCandidateRow> Resolved { get; set; } = [];

        /// <summary>判定した記事（直近分）。特別塗装でなかったものも出す</summary>
        public List<LiveryNewsRow> RecentNews { get; set; } = [];

        /// <summary>最後に記事を判定した日時。ジョブが動いているかの目安</summary>
        public DateTime? LastCheckedAt { get; set; }

        /// <summary>判定済みの記事の総数</summary>
        public int CheckedNewsCount { get; set; }

        /// <summary>GEMINI_API_KEYが設定されているか。無ければ確認そのものができない</summary>
        public bool GeminiConfigured { get; set; }

        /// <summary>いまRSSの確認が動いているか</summary>
        public bool Processing { get; set; }

        public string? Message { get; set; }

        public string? ErrorMessage { get; set; }

        /// <summary>エラーになった候補。画面でその行を目立たせる</summary>
        public int? ErrorCandidateId { get; set; }

        /// <summary>お試し判定の結果。保存しない判定でも中身を見られるように画面へ出す</summary>
        public Services.Livery.LiveryNewsTrial.Result? Trial { get; set; }

        //お試し欄は入れ直さずに試せるよう、送った内容を画面に残す
        public string? TrialUrl { get; set; }

        public string? TrialTitle { get; set; }

        public string? TrialSummary { get; set; }

        public string? TrialRegistration { get; set; }

        public string? TrialLiveryName { get; set; }
    }

    /// <summary>候補1件分。記事の情報と、いまの機体情報を並べて出す</summary>
    public class LiveryCandidateRow
    {
        public int CandidateId { get; set; }

        public string? RegistrationNumber { get; set; }

        /// <summary>未処理なら記事から読んだ塗装名、処理済みなら実際に反映した値</summary>
        public string? LiveryName { get; set; }

        /// <summary>候補を作った時点のaircraft.special_livery</summary>
        public string? CurrentLivery { get; set; }

        /// <summary>いまのaircraft.special_livery。候補を作ったあとに手で直されたら違う値になる</summary>
        public string? NowLivery { get; set; }

        /// <summary>そのレジがaircraftにあるか。無ければ承認できない</summary>
        public bool AircraftFound { get; set; }

        public string? AirlineName { get; set; }

        public string? TypeName { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime? ResolvedAt { get; set; }

        public string? NewsTitle { get; set; }

        public string? NewsUrl { get; set; }

        /// <summary>記事URLがhttpで始まるか。手動で作った候補はリンクにしない</summary>
        public bool NewsHasLink { get; set; }

        public DateTime? PublishedAt { get; set; }

        /// <summary>Geminiが書いた判定理由</summary>
        public string? Reason { get; set; }
    }

    /// <summary>判定した記事1件分</summary>
    public class LiveryNewsRow
    {
        public int NewsId { get; set; }

        public string? Title { get; set; }

        public string NewsUrl { get; set; } = string.Empty;

        public DateTime? PublishedAt { get; set; }

        public DateTime CheckedAt { get; set; }

        public bool SpecialLivery { get; set; }

        public string? Reason { get; set; }

        /// <summary>この記事から作った候補の件数</summary>
        public int CandidateCount { get; set; }

        /// <summary>
        /// 消せるか。承認・却下した候補が付いている記事は、何を承認したのか分からなくなるので消せない。
        /// </summary>
        public bool CanDelete { get; set; }

        /// <summary>記事URLがhttpで始まるか。手動で作った候補はリンクにしない</summary>
        public bool HasLink { get; set; }
    }
}
