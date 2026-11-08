using JAFleet.Models;
using JAFleet.Services;

namespace JAFleet.Test
{
    /// <summary>
    /// 更新履歴の編集（/AircraftHistory）の入力検査。
    /// DBを見ない範囲だけを対象にしている。
    /// </summary>
    [TestClass]
    public sealed class AircraftHistoryStoreTest
    {
        private static AircraftHistoryInput Valid() => new()
        {
            AhId = 1,
            Seq = 3,
            HistoryRegisterAt = new DateTime(2026, 8, 16, 10, 0, 0),
            Airline = "ANA",
            TypeDetailId = 66,
        };

        /// <summary>SEQ・航空会社・詳細型式・履歴登録日時が揃っていれば通る</summary>
        [TestMethod]
        public void PassesWithRequiredFields()
        {
            Assert.IsNull(AircraftHistoryStore.Validate(Valid()));
        }

        /// <summary>SEQは並び順そのものなので1以上を要る</summary>
        [TestMethod]
        public void RequiresPositiveSeq()
        {
            AircraftHistoryInput none = Valid();
            none.Seq = null;
            Assert.AreEqual("SEQは1以上で入力してください。", AircraftHistoryStore.Validate(none));

            AircraftHistoryInput zero = Valid();
            zero.Seq = 0;
            Assert.AreEqual("SEQは1以上で入力してください。", AircraftHistoryStore.Validate(zero));
        }

        [TestMethod]
        public void RequiresAirline()
        {
            AircraftHistoryInput input = Valid();
            input.Airline = "   ";

            Assert.AreEqual("航空会社を選択してください。", AircraftHistoryStore.Validate(input));
        }

        /// <summary>詳細型式が無いとaircraft_history_viewから落ちて詳細画面に出なくなる</summary>
        [TestMethod]
        public void RequiresTypeDetail()
        {
            AircraftHistoryInput input = Valid();
            input.TypeDetailId = 0;

            Assert.AreEqual("詳細型式を選択してください。", AircraftHistoryStore.Validate(input));
        }

        [TestMethod]
        public void RequiresHistoryRegisterAt()
        {
            AircraftHistoryInput input = Valid();
            input.HistoryRegisterAt = null;

            Assert.AreEqual("履歴登録日時を入力してください。", AircraftHistoryStore.Validate(input));
        }

        /// <summary>まとめて保存する分がすべて通れば、全体としても通る</summary>
        [TestMethod]
        public void PassesWholeBatch()
        {
            AircraftHistoryInput first = Valid();
            AircraftHistoryInput second = Valid();
            second.AhId = 2;
            second.Seq = 4;

            Assert.IsNull(AircraftHistoryStore.ValidateAll([first, second]));
        }

        /// <summary>SEQを入れ替えるときなど、送られてきた中での重複も拾う</summary>
        [TestMethod]
        public void RejectsDuplicatedSeqInBatch()
        {
            AircraftHistoryInput first = Valid();
            AircraftHistoryInput second = Valid();
            second.AhId = 2;

            AircraftHistoryStore.Result? result = AircraftHistoryStore.ValidateAll([first, second]);

            Assert.IsNotNull(result);
            Assert.AreEqual("SEQ 3 が複数の履歴で重なっています。", result.Error);
            Assert.AreEqual(1, result.ErrorAhId);
        }

        /// <summary>1行でも通らなければ、その行を指して止める</summary>
        [TestMethod]
        public void ReportsWhichRowIsInvalid()
        {
            AircraftHistoryInput ok = Valid();
            AircraftHistoryInput ng = Valid();
            ng.AhId = 7;
            ng.Seq = 4;
            ng.Airline = null;

            AircraftHistoryStore.Result? result = AircraftHistoryStore.ValidateAll([ok, ng]);

            Assert.IsNotNull(result);
            Assert.AreEqual("航空会社を選択してください。", result.Error);
            Assert.AreEqual(7, result.ErrorAhId);
        }
    }
}
