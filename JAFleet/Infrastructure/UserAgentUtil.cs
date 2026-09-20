using JAFleet.Commons.Data;
using System.Linq.Expressions;

namespace JAFleet.Infrastructure
{
    /// <summary>
    /// User-Agentの雑な分類。アクセス集計でボットを外したり端末別に数えたりするのに使う。
    /// 厳密な判定は諦めていて、目安として当たれば十分という割り切り。
    /// </summary>
    public static class UserAgentUtil
    {
        /// <summary>
        /// これが小文字のUser-Agentに含まれていたらボット扱いにする語。
        /// 集計のSQL（AccessStatsService）でもこの配列をそのまま使うので、
        /// ここへ足せば画面の「ボットを除く」にも効く。
        /// </summary>
        public static readonly string[] BOT_KEYWORDS =
        [
            "bot", "crawl", "spider", "slurp", "facebookexternalhit", "preview",
            "scrapy", "python", "curl", "wget", "go-http-client", "okhttp",
            "java/", "libwww", "http-client", "httpclient", "axios", "node-fetch",
            "headless", "phantomjs", "monitor", "uptime", "zabbix", "postman",
            "feedly", "feedfetcher", "rss", "validator", "lighthouse", "probe",
        ];

        /// <summary>User-Agentがボットのものらしいか。空のUser-Agentもボット扱いにする。</summary>
        public static bool IsBotLike(string? userAgent)
        {
            if (string.IsNullOrEmpty(userAgent))
            {
                return true;
            }

            string lower = userAgent.ToLowerInvariant();
            return BOT_KEYWORDS.Any(lower.Contains);
        }

        /// <summary>
        /// 端末の種類。EFがSQLのCASE式へ翻訳できる形にしてあるので、
        /// GROUP BYのキーとしてそのままDBへ渡せる（メモリ上でもCompileすれば同じ結果になる）。
        /// ボットを外していない集計では、ボットの多くが"PC"や"その他"に混ざる点に注意。
        /// </summary>
        public static Expression<Func<AccessLog, string>> DeviceKey { get; } = log =>
            log.UserAgent == null || log.UserAgent == "" ? "不明" :
            log.UserAgent.Contains("iPhone") || log.UserAgent.Contains("iPod") ? "スマホ" :
            log.UserAgent.Contains("iPad") ? "タブレット" :
            log.UserAgent.Contains("Android") ? (log.UserAgent.Contains("Mobile") ? "スマホ" : "タブレット") :
            log.UserAgent.Contains("Windows") || log.UserAgent.Contains("Macintosh") ||
            log.UserAgent.Contains("X11") || log.UserAgent.Contains("CrOS") ? "PC" :
            "その他";
    }
}
