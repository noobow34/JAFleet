namespace JAFleet.Models
{
    /// <summary>日別の1行</summary>
    public class DailyStat
    {
        public DateTime Date { get; set; }
        public long PageViews { get; set; }
        public int Visitors { get; set; }
    }

    /// <summary>時間帯別の1行</summary>
    public class HourlyStat
    {
        public int Hour { get; set; }
        public long PageViews { get; set; }
    }

    /// <summary>パス別の1行</summary>
    public class PathStat
    {
        public string Path { get; set; } = string.Empty;
        public long PageViews { get; set; }
        public double AvgResponseMs { get; set; }
    }

    /// <summary>「名前と件数」だけの並び（ページ種別・レジ・リファラー・端末・ステータス）</summary>
    public class CountStat
    {
        public string Name { get; set; } = string.Empty;
        public long Count { get; set; }
    }

    public class AccessStatsModel : BaseModel
    {
        /// <summary>集計対象の日数（今日を含む）</summary>
        public int Days { get; set; }
        /// <summary>ボットらしいUser-Agentも数に入れるか</summary>
        public bool IncludeBot { get; set; }
        /// <summary>管理者自身のアクセスも数に入れるか</summary>
        public bool IncludeAdmin { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        public long PageViews { get; set; }
        /// <summary>Cookieで見分けた訪問者数。Cookieを持ち回らないクライアントは入らない</summary>
        public int Visitors { get; set; }
        public int Ips { get; set; }
        public double AvgResponseMs { get; set; }
        /// <summary>ステータスコードが400以上だった件数</summary>
        public long ErrorCount { get; set; }
        /// <summary>
        /// 記録はされたがページとして数えなかった件数。
        /// 静的ファイル・JSON API・AJAX、それにroute_keyを記録する前の古い行
        /// </summary>
        public long NonPageCount { get; set; }

        public List<DailyStat> Daily { get; set; } = [];
        public List<HourlyStat> Hourly { get; set; } = [];
        public List<PathStat> TopPaths { get; set; } = [];
        public List<PathStat> SlowPaths { get; set; } = [];
        public List<CountStat> Controllers { get; set; } = [];
        public List<CountStat> TopRegistrations { get; set; } = [];
        public List<CountStat> Referers { get; set; } = [];
        public List<CountStat> Devices { get; set; } = [];
        public List<CountStat> StatusCodes { get; set; } = [];
    }
}
