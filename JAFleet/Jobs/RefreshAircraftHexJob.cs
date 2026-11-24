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
    [Description("adsb.lol の履歴ダンプ（GitHub）で機体の hex を覚え、残りを ADSBdb で引く")]
    [DisallowConcurrentExecution]
    public class RefreshAircraftHexJob : IJob
    {
        private static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(60) };

        public async Task Execute(IJobExecutionContext context)
        {
            var options = new DbContextOptionsBuilder<JAFleetContext>();
            options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            //ダンプは UTC の1日分が翌日の 12:30（日本時間）ごろに上がる。昼過ぎに動かせば前日（UTC）分がある
            var dataDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            var result = await new RefreshAircraftHex(options.Options, http).ExecuteAsync(dataDate, ct: context.CancellationToken);
            await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), result.ToSlackMessage());
        }
    }
}
