using JAFleet.Commons.Constants;
using JAFleet.Commons.EF;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace JAFleet.Jobs
{
    public class RefreshRetiredAircraftPhotoJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            var options = new DbContextOptionsBuilder<JAFleetContext>();
            options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            using JAFleetContext jContext = new(options.Options);
            var targetRegRetired = jContext.AircraftViews.Where(a => a.OperationCode == OperationCode.RETIRE_UNREGISTERED).AsNoTracking().ToArray().OrderBy(r => Guid.NewGuid());
            var refreshPhoto = new RefreshPhoto(targetRegRetired, 15);
            await refreshPhoto.ExecuteRefreshAsync();
        }
    }
}
