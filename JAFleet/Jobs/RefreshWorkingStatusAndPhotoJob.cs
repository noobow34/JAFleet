using System.ComponentModel;
using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;
using JAFleet.Batch;

namespace JAFleet.Jobs
{
    [Description("退役以外の全機体についてFlightradar24を巡回し、稼働状況と写真を更新する")]
    public class RefreshWorkingStatusAndPhotoJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            var options = new DbContextOptionsBuilder<JAFleetContext>();
            options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            using JAFleetContext jContext = new(options.Options);
            var targetReg = jContext.AircraftViews.Where(a => a.OperationCode != OperationCode.RETIRE_UNREGISTERED).AsNoTracking().ToArray().OrderBy(r => Guid.NewGuid());
            var check = new RefreshWorkingStatusAndPhoto(targetReg, 15);
            await check.ExecuteCheckAsync();
        }
    }
}
