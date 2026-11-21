using JAFleet.Commons.Data;
using JAFleet.Services;
using JAFleet.Services.Livery;

namespace JAFleet.Test
{
    /// <summary>
    /// 機体更新の予約で、DBを見ない部分（差分の取り方、反映のしかた、予約日の検査）。
    /// </summary>
    [TestClass]
    public sealed class AircraftReservationTest
    {
        private static Aircraft Current() => new()
        {
            RegistrationNumber = "JA926A",
            Airline = "ANA",
            TypeDetailId = 66,
            OperationCode = "1",
            SpecialLivery = null,
            Remarks = "",
            SeatConfig = 10,
            MaintenanceNotify = false,
        };

        /// <summary>変えた項目だけが差分になる。日時やレジは入れない</summary>
        [TestMethod]
        public void DiffTakesOnlyChangedFields()
        {
            Aircraft edited = Current();
            edited.SpecialLivery = "ポケモンジェット";
            edited.SeatConfig = 11;
            edited.UpdateTime = DateTime.Now;

            List<ReservedChange> changes = AircraftReservationStore.Diff(Current(), edited);

            CollectionAssert.AreEqual(
                new[] { nameof(Aircraft.SpecialLivery), nameof(Aircraft.SeatConfig) },
                changes.Select(c => c.Field).ToArray());
        }

        /// <summary>フォームから来る空文字とDBのnullは同じものとして扱う（触っていない欄が差分にならない）</summary>
        [TestMethod]
        public void EmptyStringEqualsNull()
        {
            Aircraft edited = Current();
            edited.Remarks = null;
            edited.SpecialLivery = "";

            Assert.AreEqual(0, AircraftReservationStore.Diff(Current(), edited).Count);
        }

        /// <summary>
        /// 予約から反映までに別の項目を直していても、予約した項目しか触らない（巻き戻らない）。
        /// </summary>
        [TestMethod]
        public void ApplyKeepsOtherFields()
        {
            Aircraft edited = Current();
            edited.SpecialLivery = "ポケモンジェット";
            string json = AircraftReservationStore.Serialize(AircraftReservationStore.Diff(Current(), edited));

            //予約したあとに運用状況が変わった
            Aircraft now = Current();
            now.OperationCode = "2";

            List<ReservedChange> applied = AircraftReservationStore.ApplyTo(now, AircraftReservationStore.Deserialize(json));

            Assert.AreEqual(1, applied.Count);
            Assert.AreEqual("ポケモンジェット", now.SpecialLivery);
            Assert.AreEqual("2", now.OperationCode);
        }

        /// <summary>数値・真偽値・nullもJSONを通して元の型で戻る</summary>
        [TestMethod]
        public void ApplyRestoresTypes()
        {
            Aircraft edited = Current();
            edited.TypeDetailId = 70;
            edited.SeatConfig = null;
            edited.MaintenanceNotify = true;
            string json = AircraftReservationStore.Serialize(AircraftReservationStore.Diff(Current(), edited));

            Aircraft target = Current();
            AircraftReservationStore.ApplyTo(target, AircraftReservationStore.Deserialize(json));

            Assert.AreEqual(70, target.TypeDetailId);
            Assert.IsNull(target.SeatConfig);
            Assert.AreEqual(true, target.MaintenanceNotify);
        }

        /// <summary>すでに予約した値になっていれば何も変えない（差分なしで終わる）</summary>
        [TestMethod]
        public void ApplyReportsNothingWhenAlreadySame()
        {
            List<ReservedChange> changes = [AircraftReservationStore.Change(nameof(Aircraft.SpecialLivery), null, "ポケモンジェット")];
            Aircraft target = Current();
            target.SpecialLivery = "ポケモンジェット";

            Assert.AreEqual(0, AircraftReservationStore.ApplyTo(target, changes).Count);
        }

        /// <summary>予約できない項目（レジなど）がJSONに紛れていても触らない</summary>
        [TestMethod]
        public void ApplyIgnoresUnreservableField()
        {
            List<ReservedChange> changes = [AircraftReservationStore.Change(nameof(Aircraft.RegistrationNumber), "JA926A", "JA999A")];
            Aircraft target = Current();

            Assert.AreEqual(0, AircraftReservationStore.ApplyTo(target, changes).Count);
            Assert.AreEqual("JA926A", target.RegistrationNumber);
        }

        /// <summary>当日分はジョブが走ったあとかもしれないので、予約は明日以降だけ</summary>
        [TestMethod]
        public void ApplyDateMustBeFuture()
        {
            DateTime today = new(2026, 11, 22, 10, 0, 0);

            Assert.IsNotNull(AircraftReservationStore.ValidateApplyDate(null, today));
            Assert.IsNotNull(AircraftReservationStore.ValidateApplyDate(today.Date, today));
            Assert.IsNotNull(AircraftReservationStore.ValidateApplyDate(today.AddDays(-1), today));
            Assert.IsNull(AircraftReservationStore.ValidateApplyDate(today.Date.AddDays(1), today));
        }

        /// <summary>Geminiが返す運航開始日。読めない書き方はnullにして人に入れてもらう</summary>
        [TestMethod]
        public void LiveryStartDateIsParsed()
        {
            Assert.AreEqual(new DateTime(2026, 12, 1), new GeminiLiveryTarget { StartDate = "2026-12-01" }.ParseStartDate());
            Assert.AreEqual(new DateTime(2026, 12, 1), new GeminiLiveryTarget { StartDate = "2026/12/1" }.ParseStartDate());
            Assert.IsNull(new GeminiLiveryTarget { StartDate = null }.ParseStartDate());
            Assert.IsNull(new GeminiLiveryTarget { StartDate = "12月上旬" }.ParseStartDate());
        }

        /// <summary>JSONのstart_dateを読める（snake_caseで返させている）</summary>
        [TestMethod]
        public void GeminiResponseHasStartDate()
        {
            GeminiLiveryResponse? response = GeminiLiveryJudge.Deserialize("""
                {"special_livery": true, "reason": "r", "targets": [{"registration": "JA926A", "livery_name": "ピカチュウジェット", "start_date": "2026-12-01"}]}
                """);

            Assert.AreEqual(new DateTime(2026, 12, 1), response!.Targets![0].ParseStartDate());
        }
    }
}
