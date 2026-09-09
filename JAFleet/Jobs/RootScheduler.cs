using JAFleet.Commons.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using Type = System.Type;

namespace JAFleet.Jobs
{
    /// <summary>
    /// scheduler_defの内容でQuartzを組み立てる。
    /// 起動時に有効な定義をまとめて登録し、以後は管理画面からの変更を1件ずつ動いているスケジューラへ反映する。
    /// </summary>
    public static class RootScheduler
    {
        private static IScheduler? sch = null;

        public static async Task CreateOrReloadRootScheduler()
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
                if (await ScheduleAsync(sc))
                {
                    Console.WriteLine($"【{sc.ClassName}:{sc.CronDef}】を登録しました。");
                }
                else
                {
                    Console.WriteLine($"【{sc.ClassName}】はクラスが見つからないため登録しませんでした。");
                }
            }
        }

        /// <summary>
        /// 1件分の定義を動いているスケジューラへ反映する。無効なら登録を消す。
        /// 実行中のジョブはQuartzでは止められないため、そのまま最後まで走る。
        /// </summary>
        public static async Task ApplyAsync(SchedulerDef def)
        {
            if (sch is null)
            {
                //起動時の読み込みがまだ終わっていない。DBには入っているので次の読み込みで登録される
                return;
            }

            //ジョブごと消してから入れ直す。cron式だけの変更でも登録し直せば次回以降が新しい式になる
            await sch.DeleteJob(new JobKey(def.ClassName));
            if (def.Enabled)
            {
                await ScheduleAsync(def);
            }
        }

        /// <summary>画面表示用に、いまスケジューラが持っているジョブの状態をクラス名で引けるようにして返す</summary>
        public static async Task<IReadOnlyDictionary<string, JobRuntimeState>> GetStatesAsync()
        {
            Dictionary<string, JobRuntimeState> states = new(StringComparer.Ordinal);
            if (sch is null)
            {
                return states;
            }

            HashSet<string> running = (await sch.GetCurrentlyExecutingJobs())
                .Select(c => c.JobDetail.Key.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (JobKey jobKey in await sch.GetJobKeys(GroupMatcher<JobKey>.AnyGroup()))
            {
                ITrigger? trigger = (await sch.GetTriggersOfJob(jobKey)).FirstOrDefault();
                states[jobKey.Name] = new JobRuntimeState(
                    true,
                    trigger?.GetNextFireTimeUtc()?.LocalDateTime,
                    trigger?.GetPreviousFireTimeUtc()?.LocalDateTime,
                    running.Contains(jobKey.Name));
            }

            //無効にした直後などは登録が無いまま動いていることがあるので、それも状態として返す
            foreach (string name in running.Where(n => !states.ContainsKey(n)))
            {
                states[name] = new JobRuntimeState(false, null, null, true);
            }

            return states;
        }

        /// <summary>定義どおりにジョブとトリガーを登録する。クラスが見つからなければ何もせずfalseを返す。</summary>
        private static async Task<bool> ScheduleAsync(SchedulerDef def)
        {
            //一覧に載っているクラスはそのまま使う。
            //一覧に無い書き方（アセンブリ修飾など）がDBに残っていても動くよう、従来どおりの解決も残す
            Type? type = JobCatalog.Find(def.ClassName)?.JobType ?? Type.GetType(def.ClassName);
            if (type is null)
            {
                return false;
            }

            var jobDetail = JobBuilder.Create(type)
                            .WithIdentity(def.ClassName)
                            .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity(def.ClassName)
                .StartNow()
                .WithCronSchedule(def.CronDef)
                .Build();

            await sch!.ScheduleJob(jobDetail, trigger);
            return true;
        }
    }

    /// <summary>スケジューラが持っているジョブの、そのときの状態</summary>
    public record JobRuntimeState(bool Scheduled, DateTime? NextFireTime, DateTime? PreviousFireTime, bool Running);
}
