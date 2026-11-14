using JAFleet.Commons.Data;
using JAFleet.Services.JcabImport;

namespace JAFleet.Test
{
    /// <summary>
    /// 航空局Excel取込で、プレビューの「登録年月日」に何を初期値として入れるか。
    /// 予約登録はF列が2段で、1行目が予約年月日（予約を受け付けた日）、2行目が納入予定年月日。
    /// 機体の登録年月日にするのは2行目の方。
    /// </summary>
    [TestClass]
    public sealed class JcabRegisterDateTest
    {
        private static JcabRecord Reservation(string reserved, string? scheduled) => new()
        {
            RecordType = JcabRecordType.Reservation,
            RegistrationNumber = "JA123A",
            RegisterDate = reserved,
            ScheduledDate = scheduled,
        };

        private static ImportPreviewItem Item(params JcabRecord[] events) => new()
        {
            RegistrationNumber = "JA123A",
            Events = [.. events],
        };

        [TestMethod]
        public void ReservationUsesScheduledDateNotReservedDate()
        {
            ImportPreviewItem item = Item(Reservation("2026/06/03", "2028/07/15"));

            Assert.AreEqual("2028/07/15", ImportPreviewBuilder.SuggestRegisterDate(item, null));
        }

        [TestMethod]
        public void ReservationKeepsMonthOnlyScheduledDate()
        {
            //予定は月までしか無いことが多い。日を勝手に補わずそのまま入れる
            ImportPreviewItem item = Item(Reservation("2026/06/03", "2028/07"));

            Assert.AreEqual("2028/07", ImportPreviewBuilder.SuggestRegisterDate(item, null));
        }

        [TestMethod]
        public void ReservationOverridesExistingAircraftDate()
        {
            ImportPreviewItem item = Item(Reservation("2026/06/03", "2028/07"));
            Aircraft aircraft = new() { RegistrationNumber = "JA123A", RegisterDate = "2027/01" };

            Assert.AreEqual("2028/07", ImportPreviewBuilder.SuggestRegisterDate(item, aircraft));
        }

        [TestMethod]
        public void ReservationWithoutScheduledDateDoesNotFallBackToReservedDate()
        {
            //予定年月日が読めなくても、予約年月日を登録年月日にしてはいけない
            ImportPreviewItem item = Item(Reservation("2026/06/03", null));

            Assert.IsNull(ImportPreviewBuilder.SuggestRegisterDate(item, null));
        }

        [TestMethod]
        public void ReservationWithoutScheduledDateKeepsExistingAircraftDate()
        {
            ImportPreviewItem item = Item(Reservation("2026/06/03", null));
            Aircraft aircraft = new() { RegistrationNumber = "JA123A", RegisterDate = "2027/01" };

            Assert.AreEqual("2027/01", ImportPreviewBuilder.SuggestRegisterDate(item, aircraft));
        }

        [TestMethod]
        public void NewRegistrationUsesRegisterDate()
        {
            //予約していた機体が実際に登録された月は、新規登録の日付で上書きされる
            ImportPreviewItem item = Item(
                Reservation("2026/06/03", "2026/09"),
                new JcabRecord { RecordType = JcabRecordType.New, RegistrationNumber = "JA123A", RegisterDate = "2026/09/18" });

            Assert.AreEqual("2026/09/18", ImportPreviewBuilder.SuggestRegisterDate(item, null));
        }

        [TestMethod]
        public void TransferKeepsExistingAircraftDate()
        {
            ImportPreviewItem item = Item(
                new JcabRecord { RecordType = JcabRecordType.Transfer, RegistrationNumber = "JA123A", RegisterDate = "2026/10/01" });
            Aircraft aircraft = new() { RegistrationNumber = "JA123A", RegisterDate = "2019/04/01" };

            Assert.AreEqual("2019/04/01", ImportPreviewBuilder.SuggestRegisterDate(item, aircraft));
        }
    }
}
