using JAFleet.Services;

namespace JAFleet.Test
{
    [TestClass]
    public sealed class WorkingStatusUpdateInfoTest
    {
        private static readonly DateTime LogDate = new(2026, 11, 24, 13, 7, 0);

        [TestMethod]
        public void adsbのログはデータ日と飛行の期間を読む()
        {
            var info = WorkingStatusUpdateInfo.FromLog(LogDate, """{"finishedAt":"2026/11/24 13:07:00","sections":[],"source":"adsb","dataDate":"2026-11-23"}""");

            Assert.IsTrue(info.IsAdsb);
            Assert.AreEqual(new DateOnly(2026, 11, 23), info.DataDate);
            Assert.AreEqual((new DateTime(2026, 11, 23, 9, 0, 0), new DateTime(2026, 11, 24, 9, 0, 0)), info.CoveredPeriod);
            Assert.AreEqual(LogDate, info.UpdatedAt);
        }

        [TestMethod]
        public void 取得元の無い古いログとJSONでないログはFR24()
        {
            var old = WorkingStatusUpdateInfo.FromLog(LogDate, """{"finishedAt":"2026/11/19 06:30:00","sections":[]}""");
            Assert.AreEqual(WorkingStatusUpdateInfo.SOURCE_FR24, old.Source);
            Assert.IsNull(old.CoveredPeriod);

            Assert.AreEqual(WorkingStatusUpdateInfo.SOURCE_FR24, WorkingStatusUpdateInfo.FromLog(LogDate, "RefreshWorkingStatus正常終了").Source);
            Assert.AreEqual(WorkingStatusUpdateInfo.SOURCE_FR24, WorkingStatusUpdateInfo.FromLog(LogDate, null).Source);
            Assert.AreEqual(WorkingStatusUpdateInfo.SOURCE_FR24,
                WorkingStatusUpdateInfo.FromLog(LogDate, """{"sections":[],"source":"fr24"}""").Source);
        }
    }
}
