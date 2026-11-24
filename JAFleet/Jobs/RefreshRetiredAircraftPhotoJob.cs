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
    [Description("退役・抹消済み機体の写真だけを FR24 から更新する（サイト設定の取得元が FR24 のときだけ動く）")]
    public class RefreshRetiredAircraftPhotoJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            //写真は FR24 の巡回で取っていたもの。取得元が FR24 でなければ動かない（FR24 への自動アクセスを避ける）。
            //いまは機体詳細は FR24 の埋め込み、LINE はリンク、OGP は自前のカード画像なので、DB の写真はどこにも出していない
            if (SiteSettings.WorkingStatusSource != WorkingStatusSource.Fr24)
            {
                this.JournalWriteLine("稼働状況の取得元が adsb.lol のため、FR24 からの写真の更新は行わない");
                return;
            }
            var options = new DbContextOptionsBuilder<JAFleetContext>();
            options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            using JAFleetContext jContext = new(options.Options);
            var targetRegRetired = jContext.AircraftViews.Where(a => a.OperationCode == OperationCode.RETIRE_UNREGISTERED).AsNoTracking().ToArray().OrderBy(r => Guid.NewGuid());
            var refreshPhoto = new RefreshPhoto(targetRegRetired, 15);
            await refreshPhoto.ExecuteRefreshAsync();
        }
    }
}
