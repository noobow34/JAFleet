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
    [Description("VRS standing-data（GitHub）で路線（callsign→空港）と空港の一覧を入れ替える。週1回で足りる")]
    [DisallowConcurrentExecution]
    public class RefreshFlightRouteJob : IJob
    {
        private static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };

        public async Task Execute(IJobExecutionContext context)
        {
            var options = new DbContextOptionsBuilder<JAFleetContext>();
            options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            string message = await new RefreshFlightRoute(options.Options, http).ExecuteAsync(context.CancellationToken);
            await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), message);
        }
    }
}
