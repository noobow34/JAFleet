using System.ComponentModel;
using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;
using JAFleet.Batch;
using JAFleet.Services;
using Noobow.Commons.Extensions;

namespace JAFleet.Jobs
{
    [Description("退役以外の全機体についてFlightradar24を巡回し、稼働状況と写真を更新する（サイト設定の取得元が FR24 のときだけ動く）")]
    public class RefreshWorkingStatusAndPhotoJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            //稼働状況の取得元が adsb.lol のあいだは FR24 を巡回しない（403 で毎日中止の通知が出るだけなので）
            if (SiteSettings.WorkingStatusSource != WorkingStatusSource.Fr24)
            {
                this.JournalWriteLine("稼働状況の取得元が adsb.lol のため、FR24 の巡回は行わない");
                return;
            }
            var options = new DbContextOptionsBuilder<JAFleetContext>();
            options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            using JAFleetContext jContext = new(options.Options);
            var targetReg = jContext.AircraftViews.Where(a => a.OperationCode != OperationCode.RETIRE_UNREGISTERED).AsNoTracking().ToArray().OrderBy(r => Guid.NewGuid());
            var check = new RefreshWorkingStatusAndPhoto(targetReg, 15);
            await check.ExecuteCheckAsync();
        }
    }
}
