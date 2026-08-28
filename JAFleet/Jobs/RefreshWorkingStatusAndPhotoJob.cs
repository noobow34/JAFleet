using JAFleet.Commons.Constants;
using JAFleet.Commons.EF;
using Microsoft.EntityFrameworkCore;
using Quartz;
using JAFleet.Batch;

namespace JAFleet.Jobs
{
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
