using JAFleet.Models;
using JAFleet.Services;

namespace JAFleet.Test
{
    /// <summary>
    /// コンフィグマスタ（/SeatConfig）の入力検査。
    /// DBを見ない範囲だけを対象にしている。
    /// </summary>
    [TestClass]
    public sealed class SeatConfigStoreTest
    {
        private static SeatConfigInput Valid() => new()
        {
            Airline = "ANA",
            Type = "B789",
            C = 48,
            PY = 21,
            Y = 146,
        };

        /// <summary>航空会社・型式と席数が1区分でも入っていれば通る</summary>
        [TestMethod]
        public void PassesWithAirlineTypeAndOneSeat()
        {
            Assert.IsNull(SeatConfigStore.Validate(Valid()));
            Assert.IsNull(SeatConfigStore.Validate(new SeatConfigInput { Airline = "JAL", Type = "B738", Y = 165 }));
        }

        /// <summary>航空会社と型式は必須</summary>
        [TestMethod]
        public void RequiresAirlineAndType()
        {
            SeatConfigInput noAirline = Valid();
            noAirline.Airline = "  ";
            Assert.AreEqual("航空会社を選択してください。", SeatConfigStore.Validate(noAirline));

            SeatConfigInput noType = Valid();
            noType.Type = null;
            Assert.AreEqual("型式を選択してください。", SeatConfigStore.Validate(noType));
        }

        /// <summary>席数が全部空欄のものは登録させない</summary>
        [TestMethod]
        public void RequiresAtLeastOneSeat()
        {
            var input = new SeatConfigInput { Airline = "ANA", Type = "B789" };

            Assert.AreEqual("席数を1つ以上入力してください。", SeatConfigStore.Validate(input));
        }

        /// <summary>空欄が「その区分なし」なので、0以下は入れさせない</summary>
        [TestMethod]
        public void RejectsZeroOrNegativeSeat()
        {
            SeatConfigInput zero = Valid();
            zero.F = 0;
            Assert.AreEqual("Fの席数は1以上で入力してください。その区分が無い場合は空欄にしてください。",
                SeatConfigStore.Validate(zero));

            SeatConfigInput negative = Valid();
            negative.Y = -1;
            Assert.AreEqual("Yの席数は1以上で入力してください。その区分が無い場合は空欄にしてください。",
                SeatConfigStore.Validate(negative));
        }

        /// <summary>打ち間違いで桁が増えたものを弾く</summary>
        [TestMethod]
        public void RejectsTooManySeats()
        {
            SeatConfigInput input = Valid();
            input.Y = SeatConfigStore.MAX_SEAT + 1;

            Assert.AreEqual($"Yの席数が大きすぎます。{SeatConfigStore.MAX_SEAT}席までです。",
                SeatConfigStore.Validate(input));
        }

        /// <summary>URLは任意。入れるならhttp(s)で、長すぎないこと。</summary>
        [TestMethod]
        public void ChecksUrlOnlyWhenEntered()
        {
            SeatConfigInput empty = Valid();
            empty.Url = "   ";
            Assert.IsNull(SeatConfigStore.Validate(empty));

            SeatConfigInput ok = Valid();
            ok.Url = "https://www.ana.co.jp/seatmap";
            Assert.IsNull(SeatConfigStore.Validate(ok));

            SeatConfigInput scheme = Valid();
            scheme.Url = "www.ana.co.jp/seatmap";
            Assert.AreEqual("URLはhttp://またはhttps://で始めてください。", SeatConfigStore.Validate(scheme));

            SeatConfigInput tooLong = Valid();
            tooLong.Url = "https://" + new string('a', SeatConfigStore.MAX_URL_LENGTH);
            Assert.AreEqual($"URLが長すぎます。{SeatConfigStore.MAX_URL_LENGTH}文字までです。",
                SeatConfigStore.Validate(tooLong));
        }
    }
}
