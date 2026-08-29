using JAFleet.Commons.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Quartz.Impl;
using Type = System.Type;

namespace JAFleet.Jobs
{
    public static class RootScheduler
    {
        private static IScheduler? sch = null;
        public static async void CreateOrReloadRootScheduler()
        {
            if (sch is not null)
            {
                await sch.Shutdown();
                sch = null;
            }

            var schedulerFactory = new StdSchedulerFactory();
            sch = await schedulerFactory.GetScheduler();
            await sch.Start();

            var options = new DbContextOptionsBuilder<JAFleetContext>();
            options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            using JAFleetContext context = new(options.Options);

            var scs = context.SchedulerDefs.Where(s => s.Enabled).AsNoTracking().ToArray();
            foreach (var sc in scs)
            {
                Type? type = Type.GetType(sc.ClassName);

                if (type is not null)
                {
                    var jobDetail = JobBuilder.Create(type)
                                    .WithIdentity(sc.ClassName)
                                    .Build();

                    var trigger = TriggerBuilder.Create()
                        .WithIdentity(sc.ClassName)
                        .StartNow()
                        .WithCronSchedule(sc.CronDef)
                        .Build();

                    await sch.ScheduleJob(jobDetail, trigger);

                    Console.WriteLine($"【{sc.ClassName}:{sc.CronDef}】を登録しました。");
                }
            }
        }
    }
}
