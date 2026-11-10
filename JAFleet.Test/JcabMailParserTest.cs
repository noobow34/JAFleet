using JAFleet.Services.JcabImport;

namespace JAFleet.Test
{
    /// <summary>
    /// Gmail取込で、メールそのものから読み取る値（対象月・再送・内線番号）の判定。
    /// 件名と本文は実際に届いているものを写している。
    /// </summary>
    [TestClass]
    public sealed class JcabMailParserTest
    {
        private const string BODY = """
            各位

            いつもお世話になっております。
            国土交通省航空局　航空機登録担当官の安達と申します。

            標記のとおり、令和8年８月分の登録情報を配信させていただきます。
            ※添付ファイルのパスワードは、「送信年月日(yyyymmdd)+_(アンダーバー)+当方の内線番号」と設定しております。

            ------------------------------------------------
            国土交通省　航空局　総務課　航空機登録担当官
            〒100-8918
            Tel: 03-5253-8111（内線48146）
            ------------------------------------------------
            ※航空機登録手続きに関するお問い合わせは
            　hqt-register48146@gxb.mlit.go.jp にお願いいたします
            """;

        [TestMethod]
        public void TargetMonthIsReadFromSubject()
        {
            Assert.AreEqual("2026/08", JcabMailParser.ParseTargetMonth("【国土交通省航空局 総務課】航空機登録情報　2026年8月分"));
            //再送でも同じように読める
            Assert.AreEqual("2026/09", JcabMailParser.ParseTargetMonth("(再送)【国土交通省航空局 総務課】航空機登録情報　2026年9月分"));
            //全角数字で書かれていても拾う
            Assert.AreEqual("2025/12", JcabMailParser.ParseTargetMonth("航空機登録情報　２０２５年１２月分"));
        }

        [TestMethod]
        public void TargetMonthIsNullWhenSubjectHasNoMonth()
        {
            Assert.IsNull(JcabMailParser.ParseTargetMonth("異動のご挨拶"));
            Assert.IsNull(JcabMailParser.ParseTargetMonth(null));
        }

        [TestMethod]
        public void ResendIsDetected()
        {
            Assert.IsTrue(JcabMailParser.IsResend("(再送)【国土交通省航空局 総務課】航空機登録情報　2026年9月分"));
            Assert.IsTrue(JcabMailParser.IsResend("【再送】【国土交通省航空局 総務課】航空機登録情報　2025年9月分"));
            Assert.IsFalse(JcabMailParser.IsResend("【国土交通省航空局 総務課】航空機登録情報　2026年8月分"));
            Assert.IsFalse(JcabMailParser.IsResend(null));
        }

        [TestMethod]
        public void ExtensionIsReadFromBody()
        {
            //署名の「内線」が先、問い合わせ先アドレスが後。同じ番号は1つにまとめる
            CollectionAssert.AreEqual(new List<string> { "48146" }, JcabMailParser.ParseExtensions(BODY));
        }

        [TestMethod]
        public void ExtensionAcceptsFullWidthDigits()
        {
            CollectionAssert.AreEqual(new List<string> { "48146" }, JcabMailParser.ParseExtensions("Tel: 03-5253-8111（内線４８１４６）"));
        }

        [TestMethod]
        public void ExtensionIsEmptyWhenBodyHasNone()
        {
            Assert.AreEqual(0, JcabMailParser.ParseExtensions("内線番号の書かれていない本文").Count);
            Assert.AreEqual(0, JcabMailParser.ParseExtensions(null).Count);
        }
    }
}
