using System.ComponentModel;
using JAFleet.Commons.Data;
using JAFleet.Services.Livery;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace JAFleet.Jobs
{
    [Description("Aviation WireのRSSを取得し、特別塗装機のニュースから機体更新の候補を作る")]
    public class CheckSpecialLiveryNewsJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            var options = new DbContextOptionsBuilder<JAFleetContext>();
            options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            using JAFleetContext jContext = new(options.Options);

            LiveryNewsChecker.Result result = await LiveryNewsChecker.ExecuteAsync(jContext);
            if (result.Error != null)
            {
                Console.WriteLine($"[CheckSpecialLiveryNewsJob] {result.Error}");
            }
        }
    }
}
