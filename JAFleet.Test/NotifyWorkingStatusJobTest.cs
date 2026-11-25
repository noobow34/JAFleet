using JAFleet;
using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using JAFleet.Jobs;
using Microsoft.EntityFrameworkCore;
using JAFleet.Batch;

namespace JAFleet.Test
{
    /// <summary>
    /// 稼働状況の通知ジョブを Quartz で本当に動かし、終わるまで待つ手動テスト。
    /// ExecuteWithtRefreshAsync は開発 DB の機体で FR24 の巡回まで動かす（JAFLEET_CONNECTION_STRING が要る）。
    /// 普段のテストでは動かさない（<see cref="ManualTest"/>）。
    /// テストを抜けるとプロセスごとジョブが中断されるので、<see cref="QuartzJobRunner"/> でジョブの終了を待つ。
    /// </summary>
    [TestClass]
    [TestCategory(ManualTest.CATEGORY)]
    public sealed class NotifyWorkingStatusJobTest
    {
        /// <summary>
        /// 待つ上限。通知ジョブは巡回が終わるのを 5 分おきに最大 36 回（3 時間）待つので、それより長くする
        /// </summary>
        private static readonly TimeSpan JobTimeout = TimeSpan.FromHours(3.5);

        [TestMethod]
        public async Task ExecuteWithoutRefreshAsync()
        {
            ManualTest.RequireOptIn();
            await QuartzJobRunner.RunOnceAsync<NotifyWorkingStatusJob>(JobTimeout);
        }

        [TestMethod]
        public async Task ExecuteWithtRefreshAsync()
        {
            ManualTest.RequireOptIn();
            var options = new DbContextOptionsBuilder<JAFleetContext>();
            options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            using JAFleetContext jContext = new(options.Options);
            var targetReg = jContext.AircraftViews.Where(a => a.OperationCode != OperationCode.RETIRE_UNREGISTERED).Take(50).AsNoTracking().ToArray().OrderBy(r => Guid.NewGuid());
            var f = new RefreshWorkingStatusAndPhoto(targetReg, 15);
            //巡回を動かしたまま通知ジョブを動かす（通知ジョブは巡回の終わりを待ってから通知する）
            Task check = f.ExecuteCheckAsync();

            await QuartzJobRunner.RunOnceAsync<NotifyWorkingStatusJob>(JobTimeout);
            //巡回の例外もここで出す
            await check;
        }
    }
}
