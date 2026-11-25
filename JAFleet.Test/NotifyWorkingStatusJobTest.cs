using JAFleet;
using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using JAFleet.Jobs;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Quartz.Impl;
using JAFleet.Batch;

namespace JAFleet.Test
{
    /// <summary>
    /// 稼働状況の通知ジョブを Quartz で本当に動かし、10 分待って様子を見る手動テスト。
    /// ExecuteWithtRefreshAsync は開発 DB の機体で FR24 の巡回まで動かす。
    /// 普段のテストでは動かさない（<see cref="ManualTest"/>）。
    /// </summary>
    [TestClass]
    [TestCategory(ManualTest.CATEGORY)]
    public sealed class NotifyWorkingStatusJobTest
    {
        [TestMethod]
        public async Task ExecuteWithoutRefreshAsync()
        {
            ManualTest.RequireOptIn();
            var schedulerFactory = new StdSchedulerFactory();
            var sch = await schedulerFactory.GetScheduler();
            await sch.Start();

            var jobDetail = JobBuilder.Create<NotifyWorkingStatusJob>()
                .WithIdentity("NotifyWorkingStatusJob")
                .Build();

            var startDate = DateTime.Now.AddSeconds(10);

            var trigger = TriggerBuilder.Create()
                .WithIdentity("NotifyWorkingStatusJob")
                .StartNow()
                .WithCronSchedule($"{startDate.Second} {startDate.Minute} {startDate.Hour} ? * * *")
                .Build();

            await sch.ScheduleJob(jobDetail, trigger);

            await Task.Delay(10 * 60 * 1000);
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
            _ = f.ExecuteCheckAsync();

            var schedulerFactory = new StdSchedulerFactory();
            var sch = await schedulerFactory.GetScheduler();
            await sch.Start();

            var jobDetail = JobBuilder.Create<NotifyWorkingStatusJob>()
                .WithIdentity("NotifyWorkingStatusJob")
                .Build();

            var startDate = DateTime.Now.AddSeconds(10);

            var trigger = TriggerBuilder.Create()
                .WithIdentity("NotifyWorkingStatusJob")
                .StartNow()
                .WithCronSchedule($"{startDate.Second} {startDate.Minute} {startDate.Hour} ? * * *")
                .Build();

            await sch.ScheduleJob(jobDetail, trigger);

            await Task.Delay(10 * 60 * 1000);
        }
    }
}
