using JAFleet.Jobs;
using Quartz;

namespace JAFleet.Test
{
    [TestClass]
    public sealed class JobCatalogTest
    {
        /// <summary>画面に出すジョブが揃っていて、説明が付いていること</summary>
        [TestMethod]
        public void AllContainsEveryJob()
        {
            var names = JobCatalog.All.Select(j => j.DisplayName).ToArray();

            CollectionAssert.Contains(names, nameof(RefreshWorkingStatusAndPhotoJob));
            CollectionAssert.Contains(names, nameof(RefreshRetiredAircraftPhotoJob));
            CollectionAssert.Contains(names, nameof(NotifyWorkingStatusJob));
            Assert.IsTrue(JobCatalog.All.All(j => !string.IsNullOrEmpty(j.Description)), "説明の無いジョブがある");
        }

        /// <summary>
        /// 画面が出すクラス名はそのまぺscheduler_defに入り、RootSchedulerが同じ名前で引き直す。
        /// 引けない形で持ってしまうと有効にしても登録されないので、往復できることを確かめる。
        /// </summary>
        [TestMethod]
        public void ClassNameResolvesToJobType()
        {
            foreach (JobInfo info in JobCatalog.All)
            {
                JobInfo? found = JobCatalog.Find(info.ClassName);

                Assert.AreSame(info, found, $"{info.ClassName} が引けない");
                Assert.IsTrue(typeof(IJob).IsAssignableFrom(info.JobType));
                //既存のscheduler_defの行と揃えるため、名前空間付きのクラス名だけを持つ（アセンブリ修飾なし）
                Assert.AreEqual(info.JobType.FullName, info.ClassName);
                Assert.IsFalse(info.ClassName.Contains(','));
            }
        }

        [TestMethod]
        public void FindReturnsNullForUnknownClass()
        {
            Assert.IsNull(JobCatalog.Find("JAFleet.Jobs.NotExistJob"));
        }
    }
}
