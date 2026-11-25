using Quartz;

namespace JAFleet.Test
{
    /// <summary>
    /// 手動テストがジョブの終了を待つ部品（<see cref="QuartzJobRunner"/>）のテスト。短いダミーのジョブで確かめる
    /// </summary>
    [TestClass]
    public sealed class QuartzJobRunnerTest
    {
        public sealed class SlowJob : IJob
        {
            public static int Finished;

            public async Task Execute(IJobExecutionContext context)
            {
                await Task.Delay(500);
                Interlocked.Increment(ref Finished);
            }
        }

        public sealed class FailingJob : IJob
        {
            public Task Execute(IJobExecutionContext context) => throw new InvalidOperationException("ジョブの中で失敗");
        }

        public sealed class EndlessJob : IJob
        {
            public Task Execute(IJobExecutionContext context) => Task.Delay(Timeout.Infinite, context.CancellationToken);
        }

        [TestMethod]
        public async Task ジョブが終わるまで待つ()
        {
            int before = SlowJob.Finished;
            await QuartzJobRunner.RunOnceAsync<SlowJob>(TimeSpan.FromSeconds(30));
            Assert.AreEqual(before + 1, SlowJob.Finished);
        }

        [TestMethod]
        public async Task ジョブの例外はそのまま投げ直す()
        {
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => QuartzJobRunner.RunOnceAsync<FailingJob>(TimeSpan.FromSeconds(30)));
            Assert.AreEqual("ジョブの中で失敗", ex.Message);
        }

        [TestMethod]
        public async Task 時間内に終わらなければ失敗にする()
        {
            await Assert.ThrowsExactlyAsync<AssertFailedException>(
                () => QuartzJobRunner.RunOnceAsync<EndlessJob>(TimeSpan.FromMilliseconds(500)));
        }
    }
}
