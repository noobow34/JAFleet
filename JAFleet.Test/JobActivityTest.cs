using JAFleet.Jobs;

namespace JAFleet.Test
{
    [TestClass]
    public sealed class JobActivityTest
    {
        [TestMethod]
        public void 途中経過は最新の1行を持ち_終われば消える()
        {
            string name = typeof(RefreshAdsbFlightJob).FullName!;
            JobActivity.Clear(typeof(RefreshAdsbFlightJob));
            Assert.IsNull(JobActivity.Progress(name));

            JobActivity.Report(typeof(RefreshAdsbFlightJob), "取得中 0.16 / 3.32 GiB（5%）");
            JobActivity.Report(typeof(RefreshAdsbFlightJob), "取得中 0.32 / 3.32 GiB（10%）");
            Assert.AreEqual("取得中 0.32 / 3.32 GiB（10%）", JobActivity.Progress(name)?.Text);

            JobActivity.Clear(typeof(RefreshAdsbFlightJob));
            Assert.IsNull(JobActivity.Progress(name));
        }

        [TestMethod]
        public void 動いていなければ実行中ではない()
        {
            foreach (var info in JobCatalog.All)
            {
                Assert.IsFalse(JobActivity.IsProcessing(info.ClassName), info.ClassName);
            }
            Assert.IsFalse(JobActivity.IsProcessing("No.Such.Job"));
        }
    }
}
