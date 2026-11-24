using System.ComponentModel;
using EnumStringValues;
using JAFleet.Batch;
using JAFleet.Commons.Data;
using JAFleet.Services;
using Microsoft.EntityFrameworkCore;
using Noobow.Commons.Constants;
using Noobow.Commons.Extensions;
using Noobow.Commons.Utils;
using Quartz;

namespace JAFleet.Jobs
{
    [Description("adsb.lol の履歴ダンプ（GitHub）の前日（UTC）分から日本の機体のフライトを adsb_flight に入れ、取得元が adsb なら稼働状況を更新して Slack に知らせる")]
    [DisallowConcurrentExecution]
    public class RefreshAdsbFlightJob : IJob
    {
        /// <summary>ダンプがまだ無いときに待つ間隔と、待つのをやめる時刻（日本時間）</summary>
        public static readonly TimeSpan RETRY_INTERVAL = TimeSpan.FromMinutes(30);
        public const int GIVE_UP_HOUR = 17;

        private static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(60) };

        public async Task Execute(IJobExecutionContext context)
        {
            //ダンプは UTC の1日分が翌日の 12:30（日本時間）ごろに上がる。昼過ぎに動かせば前日（UTC）分がある
            var dataDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            await RunAsync(dataDate, waitForDump: true, context.CancellationToken);
        }

        /// <summary>取り込みと（取得元が adsb なら）稼働状況の更新・通知。管理コンソールの手動実行からも呼ぶ</summary>
        public static async Task RunAsync(DateOnly dataDate, bool waitForDump, CancellationToken ct = default)
        {
            var options = new DbContextOptionsBuilder<JAFleetContext>();
            options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            string channel = SlackChannelEnum.jafleet.GetStringValue();
            var startedAt = DateTime.Now;
            try
            {
                //ログは全部この batch の名前と ID で出す（journalctl で1回分を追いやすいように）
                var batch = new RefreshAdsbFlight(options.Options, http);
                batch.Log($"開始: データ日 {dataDate:yyyy-MM-dd}（UTC）、取得元 {SiteSettings.WorkingStatusSource}");
                var result = await batch.ExecuteAsync(dataDate, ct);
                while (result.DumpMissing && waitForDump && DateTime.Now.Add(RETRY_INTERVAL).Hour < GIVE_UP_HOUR)
                {
                    batch.Log($"{DateTime.Now.Add(RETRY_INTERVAL):HH:mm} にもう一度確かめる（{GIVE_UP_HOUR}:00 まで待つ）");
                    await Task.Delay(RETRY_INTERVAL, ct);
                    result = await batch.ExecuteAsync(dataDate, ct);
                }
                if (result.DumpMissing)
                {
                    batch.Log("ダンプが無いまま終了。稼働状況は書き換えない");
                    await SlackUtil.PostAsync(channel, result.ToSummary() + "\n稼働状況は書き換えていません。");
                    return;
                }

                if (SiteSettings.WorkingStatusSource != WorkingStatusSource.Adsb)
                {
                    //FR24 に戻しているあいだは、フライトの記録だけ続けて稼働状況には触らない
                    batch.Log("取得元が FR24 のため、稼働状況は更新しない。終了");
                    return;
                }
                batch.Log("稼働状況を更新する");
                using var jContext = new JAFleetContext(options.Options);
                var notify = await AdsbWorkingStatus.UpdateAsync(jContext, dataDate, result.TestFlights, startedAt, ct);
                batch.Log("稼働状況を更新した: " + (notify.Sections.Count == 0
                    ? "変化なし"
                    : string.Join("、", notify.Sections.Select(x => $"{x.Title} {x.Regs.Count}"))));
                await SlackUtil.PostAsync(channel, WorkingStatusSlackMessage.BuildText(notify), WorkingStatusSlackMessage.BuildBlocks(notify));
                var total = DateTime.Now - startedAt;
                batch.Log($"Slack に投稿して終了（全体 {(int)total.TotalMinutes}分{total.Seconds:00}秒）");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await SlackUtil.PostAsync(channel, $"RefreshAdsbFlight異常終了:{DateTime.Now}\n{ex}");
                throw;
            }
            finally
            {
                //「実行中」の欄に出していた途中経過を消す
                JobActivity.Clear(typeof(RefreshAdsbFlightJob));
            }
        }
    }
}
