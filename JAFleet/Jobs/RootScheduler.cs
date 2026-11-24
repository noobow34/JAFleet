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
            Dictionary<string, DateTime> running = new(StringComparer.Ordinal);
            if (sch is not null)
            {
                foreach (var c in await sch.GetCurrentlyExecutingJobs())
                {
                    running[c.JobDetail.Key.Name] = c.FireTimeUtc.LocalDateTime;
                }

                foreach (JobKey jobKey in await sch.GetJobKeys(GroupMatcher<JobKey>.AnyGroup()))
                {
                    //「今すぐ実行」で足しただけのジョブ（トリガー無し）は、定期実行には載っていない
                    ITrigger? trigger = (await sch.GetTriggersOfJob(jobKey)).FirstOrDefault(t => t is ICronTrigger);
                    states[jobKey.Name] = new JobRuntimeState(
                        trigger != null,
                        trigger?.GetNextFireTimeUtc()?.LocalDateTime,
                        trigger?.GetPreviousFireTimeUtc()?.LocalDateTime,
                        running.ContainsKey(jobKey.Name),
                        running.TryGetValue(jobKey.Name, out var since) ? since : null);
                }
            }

            //無効にした直後などは登録が無いまま動いていることがあるので、それも状態として返す。
            //管理コンソールのボタンから直接動かしたもの（Quartz を通らない）も実行中として出す
            foreach (JobInfo info in JobCatalog.All)
            {
                bool quartzRunning = running.TryGetValue(info.ClassName, out var since);
                bool processing = JobActivity.IsProcessing(info.ClassName);
                if (!quartzRunning && !processing)
                {
                    continue;
                }
                states.TryGetValue(info.ClassName, out var s0);
                states[info.ClassName] = (s0 ?? new JobRuntimeState(false, null, null, false, null)) with
                {
                    Running = true,
                    RunningSince = quartzRunning ? since : s0?.RunningSince,
                };
            }

            return states;
        }

        /// <summary>
        /// ジョブを今すぐ1回だけ動かす。無効（定期実行に載っていない）ジョブでも動かせる。
        /// 実行中なら動かさずに理由を返す。動かせたら null。
        /// </summary>
        public static async Task<string?> RunNowAsync(string className)
        {
            if (sch is null)
            {
                return "スケジューラがまだ起動していません。少し待ってからやり直してください。";
            }
            JobInfo? info = JobCatalog.Find(className);
            if (info == null)
            {
                return $"クラス {className} が見つかりません。";
            }
            var states = await GetStatesAsync();
            if (states.TryGetValue(className, out var state) && state.Running)
            {
                return $"{info.DisplayName} はいま実行中です。終わってからやり直してください。";
            }

            var key = new JobKey(className);
            if (!await sch.CheckExists(key))
            {
                //定期実行に載っていないジョブは、トリガー無しで置いておけるようにして足す
                //（あとで有効にしたときは ApplyAsync が消して登録し直す）
                var job = JobBuilder.Create(info.JobType).WithIdentity(key).StoreDurably().Build();
                await sch.AddJob(job, replace: false);
            }
            await sch.TriggerJob(key);
            return null;
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
    /// <param name="Scheduled">定期実行（cron のトリガー）に載っているか</param>
    /// <param name="RunningSince">実行中なら、始まった時刻（Quartz から動いたときだけ分かる）</param>
    public record JobRuntimeState(bool Scheduled, DateTime? NextFireTime, DateTime? PreviousFireTime, bool Running, DateTime? RunningSince);
}
