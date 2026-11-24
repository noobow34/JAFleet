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
    [Description("hex（ICAO 24bit アドレス）の無い機体を ADSBdb で引いて補う。ダンプのレジで覚えるのは RefreshAdsbFlightJob がやるので、月1回程度で足りる")]
    [DisallowConcurrentExecution]
    public class RefreshAircraftHexJob : IJob
    {
        private static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(1) };

        public async Task Execute(IJobExecutionContext context)
        {
            var options = new DbContextOptionsBuilder<JAFleetContext>();
            options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            var result = await new RefreshAircraftHex(options.Options, http).ExecuteAsync(context.CancellationToken);
            await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), result.ToSlackMessage());
        }
    }
}
