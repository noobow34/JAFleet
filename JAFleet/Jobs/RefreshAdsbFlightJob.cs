using System.ComponentModel;
using EnumStringValues;
using JAFleet.Batch;
using JAFleet.Commons.Data;
using Microsoft.EntityFrameworkCore;
using Noobow.Commons.Constants;
using Noobow.Commons.Utils;
using Quartz;

namespace JAFleet.Jobs
{
    [Description("adsb.lol の履歴ダンプ（GitHub）の前日（UTC）分から、日本の機体のフライトを adsb_flight に入れる（試運転中。稼働状況はまだ変えない）")]
    [DisallowConcurrentExecution]
    public class RefreshAdsbFlightJob : IJob
    {
        private static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(60) };

        public async Task Execute(IJobExecutionContext context)
        {
            var options = new DbContextOptionsBuilder<JAFleetContext>();
            options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            //ダンプは UTC の1日分が翌日の 12:30（日本時間）ごろに上がる。昼過ぎに動かせば前日（UTC）分がある
            var dataDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            var result = await new RefreshAdsbFlight(options.Options, http).ExecuteAsync(dataDate, context.CancellationToken);
            //試運転中は、ダンプが無かったときだけ知らせる（取り込めた日は記録を見て確かめる）
            if (result.DumpMissing)
            {
                await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), result.ToSummary());
            }
        }
    }
}
