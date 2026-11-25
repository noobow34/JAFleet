using System.Collections.Specialized;
using Quartz;
using Quartz.Impl;
using Quartz.Listener;

namespace JAFleet.Test
{
    /// <summary>
    /// 手動テストから Quartz のジョブを 1 回だけ動かし、終わるまで待つ。
    /// テストのメソッドを抜けるとプロセスが終わり、動いているジョブが中断されるので、決め打ちの時間ではなく
    /// ジョブの終了（IJobListener.JobWasExecuted）を待つ。ジョブが例外を出したらそのまま投げ直す。
    /// </summary>
    internal static class QuartzJobRunner
    {
        public static async Task RunOnceAsync<TJob>(TimeSpan timeout) where TJob : IJob
        {
            //スケジューラは毎回別名で作る（同じ名前だと StdSchedulerFactory が前のものを使い回す）
            var props = new NameValueCollection
            {
                ["quartz.scheduler.instanceName"] = $"ManualTest-{Guid.NewGuid():N}",
                ["quartz.threadPool.threadCount"] = "1",
            };
            IScheduler scheduler = await new StdSchedulerFactory(props).GetScheduler();
            var listener = new CompletionListener();
            scheduler.ListenerManager.AddJobListener(listener);
            try
            {
                await scheduler.Start();
                IJobDetail job = JobBuilder.Create<TJob>().WithIdentity(typeof(TJob).Name).Build();
                ITrigger trigger = TriggerBuilder.Create().WithIdentity(typeof(TJob).Name).StartNow().Build();
                await scheduler.ScheduleJob(job, trigger);

                Task finished = await Task.WhenAny(listener.Completed, Task.Delay(timeout));
                if (finished != listener.Completed)
                {
                    Assert.Fail($"{typeof(TJob).Name} が {timeout} 以内に終わりませんでした");
                }
                await listener.Completed; //ジョブの例外はここで投げ直される
            }
            finally
            {
                //ジョブが終わっていれば待たずに閉じる（タイムアウトのときは中断させる）
                await scheduler.Shutdown(waitForJobsToComplete: false);
            }
        }

        private sealed class CompletionListener : JobListenerSupport
        {
            private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task Completed => _completed.Task;

            public override string Name => nameof(CompletionListener);

            public override Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
            {
                if (jobException != null)
                {
                    //Quartz はジョブの例外を JobExecutionException と JobExecutionProcessException（どちらも SchedulerException）で包むので、中身まではがす
                    Exception ex = jobException;
                    while (ex is SchedulerException && ex.InnerException != null)
                    {
                        ex = ex.InnerException;
                    }
                    _completed.TrySetException(ex);
                }
                else
                {
                    _completed.TrySetResult();
                }
                return Task.CompletedTask;
            }
        }
    }
}
