using System.Text.Json;
using JAFleet.Commons.Data;
using JAFleet.Commons.Util;
using JAFleet.Services;

namespace JAFleet.Test
{
    /// <summary>
    /// 登録年月日・抹消年月日（DateOnly）と "yyyy/MM/dd" の文字列の変換。
    /// 月まで・年までの登録はやめたので、古い形はその月・その年の末日にする（DB の移行と同じ）。
    /// </summary>
    [TestClass]
    public sealed class JaDateUtilTest
    {
        [TestMethod]
        [DataRow("2019/03/15", 2019, 3, 15)]
        [DataRow("2019-03-15", 2019, 3, 15)]
        [DataRow(" 2019/3/5 ", 2019, 3, 5)]
        [DataRow("2025/05/xx", 2025, 5, 31)]
        [DataRow("2019/03/--", 2019, 3, 31)]
        [DataRow("2026/02", 2026, 2, 28)]
        [DataRow("2024/02/xx", 2024, 2, 29)]
        [DataRow("2020/--/--", 2020, 12, 31)]
        [DataRow("2020", 2020, 12, 31)]
        public void ParsesToDate(string text, int y, int m, int d)
        {
            Assert.IsTrue(JaDateUtil.TryParse(text, out DateOnly? date));
            Assert.AreEqual(new DateOnly(y, m, d), date);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void BlankIsNull(string? text)
        {
            Assert.IsTrue(JaDateUtil.TryParse(text, out DateOnly? date));
            Assert.IsNull(date);
        }

        [TestMethod]
        [DataRow("2019/02/30")]
        [DataRow("2019/13/01")]
        [DataRow("納入予定")]
        [DataRow("2025/04/031")]
        public void RejectsInvalid(string text)
        {
            Assert.IsFalse(JaDateUtil.TryParse(text, out _));
            Assert.IsNull(JaDateUtil.ParseOrNull(text));
        }

        [TestMethod]
        public void FormatsWithSlashes()
        {
            Assert.AreEqual("2019/03/05", JaDateUtil.Format(new DateOnly(2019, 3, 5)));
            Assert.IsNull(JaDateUtil.Format(null));
        }

        /// <summary>JSON API の registerDate は文字列だったころと同じ "yyyy/MM/dd" で出す</summary>
        [TestMethod]
        public void JsonWritesSlashFormatAndReadsOldForms()
        {
            JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { Converters = { new SlashDateOnlyJsonConverter() } };
            AircraftView av = new() { RegistrationNumber = "JA381A", RegisterDate = new DateOnly(2019, 3, 15), DeregisterDate = null };

            string json = JsonSerializer.Serialize(av, options);
            StringAssert.Contains(json, "\"registerDate\":\"2019/03/15\"");
            StringAssert.Contains(json, "\"deregisterDate\":null");

            Assert.AreEqual(new DateOnly(2019, 3, 15), JsonSerializer.Deserialize<DateOnly?>("\"2019-03-15\"", options));
            Assert.AreEqual(new DateOnly(2025, 5, 31), JsonSerializer.Deserialize<DateOnly?>("\"2025/05/xx\"", options));
            Assert.IsNull(JsonSerializer.Deserialize<DateOnly?>("null", options));
        }

        /// <summary>
        /// register_date が文字列だったころに積んだ予約（after が "2026/08/xx" などの文字列）も反映できる。
        /// 新しく積む予約は "yyyy/MM/dd" で持つ。
        /// </summary>
        [TestMethod]
        public void ReservationReadsOldStringDatesAndWritesSlashFormat()
        {
            Aircraft aircraft = new() { RegistrationNumber = "JA01XJ", Airline = "JAL", TypeDetailId = 1 };
            List<ReservedChange> old = AircraftReservationStore.Deserialize(
                "[{\"field\":\"RegisterDate\",\"before\":\"2026/07/xx\",\"after\":\"2026/08/xx\"}]");

            List<ReservedChange> applied = AircraftReservationStore.ApplyTo(aircraft, old);

            Assert.AreEqual(1, applied.Count);
            Assert.AreEqual(new DateOnly(2026, 8, 31), aircraft.RegisterDate);

            Aircraft edited = new() { RegistrationNumber = "JA01XJ", Airline = "JAL", TypeDetailId = 1, RegisterDate = aircraft.RegisterDate, DeregisterDate = new DateOnly(2030, 1, 2) };
            List<ReservedChange> diff = AircraftReservationStore.Diff(aircraft, edited);

            Assert.AreEqual(1, diff.Count);
            Assert.AreEqual(nameof(Aircraft.DeregisterDate), diff[0].Field);
            Assert.AreEqual("2030/01/02", diff[0].After.GetString());
        }
    }
}
