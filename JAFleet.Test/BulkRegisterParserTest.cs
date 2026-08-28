using JAFleet.Classes.BulkRegister;

namespace JAFleet.Test
{
    [TestClass]
    public sealed class BulkRegisterParserTest
    {
        /// <summary>Excelから2列コピーするとタブ区切りで入る</summary>
        [TestMethod]
        public void ParseTabSeparated()
        {
            var lines = BulkRegisterParser.Parse("JA801A\t2026/04/01\nJA802A\t2026/04/15");

            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual("JA801A", lines[0].RegistrationNumber);
            Assert.AreEqual("2026/04/01", lines[0].RegisterDate);
            Assert.AreEqual("JA802A", lines[1].RegistrationNumber);
            Assert.AreEqual("2026/04/15", lines[1].RegisterDate);
            Assert.IsTrue(lines.All(l => l.IsValid));
        }

        /// <summary>区切りはカンマでも空白でもよい。3列目は製造番号。</summary>
        [TestMethod]
        public void ParseCommaAndSpaceSeparated()
        {
            var lines = BulkRegisterParser.Parse("JA801A,2026/04/01,65001\nJA802A 2026/04/15 65002");

            Assert.AreEqual("65001", lines[0].SerialNumber);
            Assert.AreEqual("2026/04/15", lines[1].RegisterDate);
            Assert.AreEqual("65002", lines[1].SerialNumber);
        }

        /// <summary>JAを省いても、全角で書いても、小文字でも同じレジになる</summary>
        [TestMethod]
        public void NormalizeRegistration()
        {
            var lines = BulkRegisterParser.Parse("801A\nja802a\nＪＡ８０３Ａ");

            Assert.AreEqual("JA801A", lines[0].RegistrationNumber);
            Assert.AreEqual("JA802A", lines[1].RegistrationNumber);
            Assert.AreEqual("JA803A", lines[2].RegistrationNumber);
        }

        /// <summary>和暦や区切り違いの日付も yyyy/MM/dd に寄せる</summary>
        [TestMethod]
        public void NormalizeRegisterDate()
        {
            var lines = BulkRegisterParser.Parse("JA801A\tR8.5.20\nJA802A\t2026-4-1\nJA803A\t令和8年5月30日");

            Assert.AreEqual("2026/05/20", lines[0].RegisterDate);
            Assert.AreEqual("2026/04/01", lines[1].RegisterDate);
            Assert.AreEqual("2026/05/30", lines[2].RegisterDate);
        }

        /// <summary>
        /// 区切りを2つ続けると登録年月日だけ空にできる。
        /// 登録年月日は分からないが製造番号は分かっている、という登録のため。
        /// </summary>
        [TestMethod]
        public void ReadSerialNumberWithoutRegisterDate()
        {
            var lines = BulkRegisterParser.Parse("JA801A,,65001\nJA802A\t\t65002");

            Assert.IsTrue(lines[0].IsValid);
            Assert.IsNull(lines[0].RegisterDate);
            Assert.AreEqual("65001", lines[0].SerialNumber);

            Assert.IsTrue(lines[1].IsValid);
            Assert.IsNull(lines[1].RegisterDate);
            Assert.AreEqual("65002", lines[1].SerialNumber);
        }

        /// <summary>登録年月日は1機ずつ違うので、行ごとの値をそのまま使う</summary>
        [TestMethod]
        public void ReadRegisterDatePerLine()
        {
            var lines = BulkRegisterParser.Parse("JA801A\t2026/04/01\nJA802A\t2026/04/15\nJA803A\t2026/05/02");

            Assert.AreEqual("2026/04/01", lines[0].RegisterDate);
            Assert.AreEqual("2026/04/15", lines[1].RegisterDate);
            Assert.AreEqual("2026/05/02", lines[2].RegisterDate);
        }

        /// <summary>書かなかった行の登録年月日は空のまま。エラーにはしない。</summary>
        [TestMethod]
        public void AllowEmptyRegisterDate()
        {
            var lines = BulkRegisterParser.Parse("JA801A\nJA802A\t");

            Assert.IsTrue(lines[0].IsValid);
            Assert.IsNull(lines[0].RegisterDate);
            Assert.IsTrue(lines[1].IsValid);
            Assert.IsNull(lines[1].RegisterDate);
        }

        /// <summary>空行は読み飛ばすが、行番号は入力欄の見た目と合わせる</summary>
        [TestMethod]
        public void SkipEmptyLinesKeepingLineNumber()
        {
            var lines = BulkRegisterParser.Parse("JA801A\n\n   \nJA802A");

            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(1, lines[0].LineNumber);
            Assert.AreEqual(4, lines[1].LineNumber);
        }

        /// <summary>
        /// yyyy/MM/dd に直せない日付はエラーにせず書いたまま通す。
        /// 予定月までしか分かっていない「2026/08/xx」のような登録があるため。
        /// </summary>
        [TestMethod]
        public void KeepUnparsableDateAsIs()
        {
            var lines = BulkRegisterParser.Parse("JA801A\t2026/08/xx\nJA802A\t納入予定\nJA803A\t2026/04/01");

            Assert.IsTrue(lines[0].IsValid);
            Assert.AreEqual("2026/08/xx", lines[0].RegisterDate);
            Assert.IsTrue(lines[0].RegisterDateAsIs);

            Assert.IsTrue(lines[1].IsValid);
            Assert.AreEqual("納入予定", lines[1].RegisterDate);
            Assert.IsTrue(lines[1].RegisterDateAsIs);

            //直せた行にはAsIsの印を付けない
            Assert.AreEqual("2026/04/01", lines[2].RegisterDate);
            Assert.IsFalse(lines[2].RegisterDateAsIs);
        }

        /// <summary>解釈できない行はエラーとして残す。捨てると気付けない。</summary>
        [TestMethod]
        public void KeepInvalidLinesWithError()
        {
            var lines = BulkRegisterParser.Parse("これはレジではない\nJA803A\t2026/04/01");

            Assert.AreEqual(2, lines.Count);
            Assert.IsFalse(lines[0].IsValid);
            Assert.IsTrue(lines[1].IsValid);
        }

        /// <summary>同じレジを2回書いたら後の行をエラーにする</summary>
        [TestMethod]
        public void DetectDuplicatedRegistration()
        {
            var lines = BulkRegisterParser.Parse("JA801A\t2026/04/01\nJA801A\t2026/04/02");

            Assert.IsTrue(lines[0].IsValid);
            Assert.IsFalse(lines[1].IsValid);
        }

        /// <summary>列が多い行は、黙って捨てずにエラーにする</summary>
        [TestMethod]
        public void RejectTooManyColumns()
        {
            var lines = BulkRegisterParser.Parse("JA801A\t2026/04/01\t65001\tよけいな列");

            Assert.IsFalse(lines[0].IsValid);
        }

        [TestMethod]
        public void ParseEmptyInput()
        {
            Assert.AreEqual(0, BulkRegisterParser.Parse(null).Count);
            Assert.AreEqual(0, BulkRegisterParser.Parse("  \n \n").Count);
        }
    }
}
