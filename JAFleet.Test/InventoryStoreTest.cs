using JAFleet.Commons.Data;
using JAFleet.Services;

namespace JAFleet.Test
{
    [TestClass]
    public sealed class InventoryStoreTest
    {
        private static readonly DateTime T0 = new(2026, 11, 27, 9, 0, 0);

        private static AircraftView Aircraft(string reg, DateTime? updated) =>
            new() { RegistrationNumber = reg, ActualUpdateTime = updated };

        private static InventorySession NewSession() =>
            new() { Name = "テスト", ConditionJson = "{}", CreatedAt = T0, LastOpenedAt = T0 };

        [TestMethod]
        public void 全部確認済になったら自動で完了する()
        {
            var session = NewSession();
            var aircraft = new[] { Aircraft("JA801A", T0), Aircraft("JA802A", T0) };
            var checks = new Dictionary<string, DateTime> { ["JA801A"] = T0.AddHours(1), ["JA802A"] = T0.AddHours(1) };

            InventoryStore.UpdateProgress(session, aircraft, checks, T0.AddHours(2));

            Assert.AreEqual(2, session.TotalCount);
            Assert.AreEqual(2, session.CheckedCount);
            Assert.AreEqual(T0.AddHours(2), session.CompletedAt);
            Assert.IsFalse(session.CompletedManually);
        }

        [TestMethod]
        public void 要再確認があるうちは完了しない()
        {
            var session = NewSession();
            //JA802A は目印のあとで更新された
            var aircraft = new[] { Aircraft("JA801A", T0), Aircraft("JA802A", T0.AddHours(3)) };
            var checks = new Dictionary<string, DateTime> { ["JA801A"] = T0.AddHours(1), ["JA802A"] = T0.AddHours(1) };

            InventoryStore.UpdateProgress(session, aircraft, checks, T0.AddHours(4));

            Assert.AreEqual(1, session.CheckedCount);
            Assert.IsNull(session.CompletedAt);
        }

        [TestMethod]
        public void 機体が無い棚卸は完了しない()
        {
            var session = NewSession();
            InventoryStore.UpdateProgress(session, [], new Dictionary<string, DateTime>(), T0);
            Assert.IsNull(session.CompletedAt);
        }

        [TestMethod]
        public void 完了のあとで未確認が出ても完了のまま()
        {
            var session = NewSession();
            session.CompletedAt = T0;
            session.CompletedManually = true;
            var aircraft = new[] { Aircraft("JA801A", T0), Aircraft("JA803A", T0) };
            var checks = new Dictionary<string, DateTime> { ["JA801A"] = T0.AddHours(1) };

            InventoryStore.UpdateProgress(session, aircraft, checks, T0.AddHours(2));

            Assert.AreEqual(1, session.CheckedCount);
            Assert.AreEqual(T0, session.CompletedAt);
            Assert.IsTrue(session.CompletedManually);
        }

        [TestMethod]
        public void 保存した条件のJSONを絞り込みの形に戻す()
        {
            var model = InventoryStore.ToSearchModel("""{"RegistrationNumber":"JA80.*","Airline":"ANA|ADO","RegistrationSelection":"2"}""");
            Assert.AreEqual("JA80.*", model.RegistrationNumber);
            Assert.AreEqual("ANA|ADO", model.Airline);
            Assert.AreEqual("2", model.RegistrationSelection);
            Assert.AreEqual("0", model.DeregistrationSelection, "書いていない項目は既定値");
        }
    }
}
