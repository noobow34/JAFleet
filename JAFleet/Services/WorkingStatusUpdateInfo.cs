using System.Text.Json;
using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using JAFleet.Models;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Services
{
    /// <summary>
    /// 稼働状況を最後に更新したのは、いつ・どのジョブ（adsb.lol / FR24）か。
    /// 稼働チェックログ（log の WORKING_INFO）の最新の1件から読む。
    /// </summary>
    public record WorkingStatusUpdateInfo(DateTime UpdatedAt, string Source, DateOnly? DataDate)
    {
        public const string SOURCE_ADSB = "adsb";
        public const string SOURCE_FR24 = "fr24";

        public bool IsAdsb => Source == SOURCE_ADSB;

        /// <summary>adsb.lol のデータが表す飛行の期間（日本時間。データ日の 9:00〜翌日 9:00）</summary>
        public (DateTime From, DateTime To)? CoveredPeriod => DataDate is DateOnly d
            ? (d.ToDateTime(new TimeOnly(9, 0)), d.AddDays(1).ToDateTime(new TimeOnly(9, 0)))
            : null;

        /// <summary>最新の稼働チェックログから読む。1件も無ければ null</summary>
        public static WorkingStatusUpdateInfo? Latest(JAFleetContext context)
        {
            var log = context.Logs.AsNoTracking()
                .Where(l => l.LogType == LogType.WORKING_INFO)
                .OrderByDescending(l => l.LogDate)
                .Select(l => new { l.LogDate, l.LogDetail })
                .FirstOrDefault();
            return log?.LogDate == null ? null : FromLog(log.LogDate.Value, log.LogDetail);
        }

        /// <summary>
        /// ログ1件から作る。取得元の項目が無い（入れる前の FR24 の巡回で書いた）ログや、
        /// JSON でない古い形式のログは FR24 とみなす
        /// </summary>
        public static WorkingStatusUpdateInfo FromLog(DateTime logDate, string? logDetail)
        {
            WorkingCheckLogJson? json = null;
            if (!string.IsNullOrWhiteSpace(logDetail) && logDetail.TrimStart().StartsWith('{'))
            {
                try
                {
                    json = JsonSerializer.Deserialize<WorkingCheckLogJson>(logDetail);
                }
                catch (JsonException)
                {
                }
            }
            string source = json?.Source == SOURCE_ADSB ? SOURCE_ADSB : SOURCE_FR24;
            DateOnly? dataDate = source == SOURCE_ADSB && DateOnly.TryParse(json?.DataDate, out var d) ? d : null;
            return new WorkingStatusUpdateInfo(logDate, source, dataDate);
        }
    }
}
