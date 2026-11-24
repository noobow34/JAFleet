using System.ComponentModel;
using EnumStringValues;
using JAFleet.Commons.Data;
using JAFleet.Services;
using Microsoft.EntityFrameworkCore;
using Noobow.Commons.Constants;
using Noobow.Commons.Utils;
using Quartz;

namespace JAFleet.Jobs
{
    [Description("機体更新の予約のうち、反映日が今日以前のものをaircraftへ反映する")]
    public class ApplyAircraftReservationJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            var options = new DbContextOptionsBuilder<JAFleetContext>();
            options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            using JAFleetContext jContext = new(options.Options);

            await RunAsync(jContext);
        }

        /// <summary>反映して、結果をSlackへ知らせ、反映した機体の写真を取り直す。画面の「いま反映する」からも呼ぶ</summary>
        public static async Task<List<AircraftReservationStore.ApplyOutcome>?> RunAsync(JAFleetContext context)
        {
            List<AircraftReservationStore.ApplyOutcome>? outcomes = await AircraftReservationStore.ApplyDueAsync(context, DateTime.Now);
            if (outcomes == null)
            {
                Console.WriteLine("[AircraftReservation] すでに実行中のため何もしませんでした。");
                return null;
            }
            Console.WriteLine($"[AircraftReservation] 予約{outcomes.Count}件を処理しました。");

            string? text = AircraftReservationStore.BuildSlackText(outcomes, Services.Livery.LiveryNewsConstant.SITE_URL);
            if (text != null)
            {
                try
                {
                    await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), text);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AircraftReservation] Slack通知に失敗しました: {ex.Message}");
                }
            }

            return outcomes;
        }
    }
}
