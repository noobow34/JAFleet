namespace JAFleet.Models
{
    /// <summary>定期実行ジョブの一覧画面</summary>
    public class SchedulerModel : BaseModel
    {
        public List<SchedulerRow> Rows { get; set; } = [];
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>ジョブ1件分。scheduler_defの内容と、動いているスケジューラでの状態を合わせて持つ。</summary>
    public class SchedulerRow
    {
        public required string ClassName { get; set; }

        /// <summary>クラス名から名前空間を除いたもの</summary>
        public required string DisplayName { get; set; }

        public string? Description { get; set; }

        public string CronDef { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        /// <summary>scheduler_defに行があるか。無い場合は保存したときに追加される。</summary>
        public bool Defined { get; set; }

        /// <summary>クラスがこのアセンブリに見つかるか。見つからないものは有効にできない。</summary>
        public bool TypeFound { get; set; }

        /// <summary>いまスケジューラに登録されているか</summary>
        public bool Scheduled { get; set; }

        /// <summary>いま実行中か</summary>
        public bool Running { get; set; }

        public DateTime? NextFireTime { get; set; }

        public DateTime? PreviousFireTime { get; set; }

        /// <summary>実行中なら、始まった時刻（Quartz から動いたときだけ分かる）</summary>
        public DateTime? RunningSince { get; set; }

        /// <summary>長い処理の途中経過の最新の1行と、その時刻</summary>
        public string? Progress { get; set; }
        public DateTime? ProgressAt { get; set; }
    }
}
