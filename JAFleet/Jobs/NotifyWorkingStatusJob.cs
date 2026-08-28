using EnumStringValues;
using JAFleet.Batch;
using JAFleet.Commons.Constants;
using JAFleet.Commons.EF;
using Microsoft.EntityFrameworkCore;
using Noobow.Commons.Constants;
using Noobow.Commons.Utils;
using Quartz;

namespace JAFleet.Jobs
{
    public class NotifyWorkingStatusJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            DbContextOptionsBuilder<JAFleetContext>? Options = new();
            Options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            using JAFleetContext jc = new (Options!.Options);
            var log = jc.Logs.Where(l => l.LogType == LogType.WORKING_NOTIFY_TEXT && l.LogDate!.Value.Date == DateTime.Now.Date).OrderByDescending(l => l.LogId).FirstOrDefault();
            if (log != null) {
                await PostAsync(log.LogDetail!);
            }
            else
            {
                if (RefreshWorkingStatusAndPhoto.Processing)
                {
                    await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), "RefreshWorkingStatusAndPhoto実行中です。終了したら通知します。");
                    int waitCount = 0;
                    while (RefreshWorkingStatusAndPhoto.Processing)
                    {
                        await Task.Delay(5 * 60 * 1_000);
                        waitCount++;
                        if (waitCount > 36)
                        {
                            await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), "RefreshWorkingStatusAndPhotoが完了しないため処理を中止します。");
                            return;
                        }
                    }
                    var log2 = jc.Logs.Where(l => l.LogType == LogType.WORKING_NOTIFY_TEXT && l.LogDate!.Value.Date == DateTime.Now.Date).OrderByDescending(l => l.LogId).FirstOrDefault();
                    if (log2 != null)
                    {
                        await PostAsync(log2.LogDetail!);
                    }
                }
                else
                {
                    await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), "本日はRefreshWorkingStatusAndPhotoが実行されていません。状況を確認してください。");
                }
            }
        }

        /// <summary>通知内容のJSONならBlockKitで、旧形式のテキストならそのまま投稿する</summary>
        private static async Task PostAsync(string logDetail)
        {
            var notify = WorkingStatusSlackMessage.TryParse(logDetail);
            if (notify == null)
            {
                await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), logDetail);
                return;
            }

            await SlackUtil.PostAsync(
                SlackChannelEnum.jafleet.GetStringValue(),
                WorkingStatusSlackMessage.BuildText(notify),
                WorkingStatusSlackMessage.BuildBlocks(notify));
        }
    }
}
