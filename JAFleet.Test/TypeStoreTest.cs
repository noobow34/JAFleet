using JAFleet.Services;

namespace JAFleet.Test
{
    /// <summary>
    /// 型式のその場登録（TypeStore）の入力検査。
    /// DBを見ない範囲だけを対象にしている。
    /// </summary>
    [TestClass]
    public sealed class TypeStoreTest
    {
        /// <summary>型式コードと型式名があれば通る。表示順は任意。</summary>
        [TestMethod]
        public void PassesWithCodeAndName()
        {
            Assert.IsNull(TypeStore.Validate("B789", "787-9", null));
            Assert.IsNull(TypeStore.Validate("B789", "787-9", "0310"));
            Assert.IsNull(TypeStore.Validate(" B789 ", " 787-9 ", " "));
        }

        /// <summary>型式コードと型式名は必須</summary>
        [TestMethod]
        public void RequiresCodeAndName()
        {
            Assert.AreEqual("型式コードを入力してください。", TypeStore.Validate("  ", "787-9", null));
            Assert.AreEqual("型式コードを入力してください。", TypeStore.Validate(null, "787-9", null));
            Assert.AreEqual("型式名を入力してください。", TypeStore.Validate("B789", " ", null));
            Assert.AreEqual("型式名を入力してください。", TypeStore.Validate("B789", null, null));
        }

        /// <summary>
        /// 型式コードは主キーで `/Aircraft/Type/{型式コード}` のURLにも入るので、
        /// 半角英数字と - _ . 以外は通さない。
        /// </summary>
        [TestMethod]
        public void RejectsSymbolInCode()
        {
            const string message = "型式コードは半角英数字と - _ . だけで入力してください。";

            Assert.AreEqual(message, TypeStore.Validate("B7 89", "787-9", null));
            Assert.AreEqual(message, TypeStore.Validate("B789/A", "787-9", null));
            Assert.AreEqual(message, TypeStore.Validate("ボーイング", "787-9", null));

            Assert.IsNull(TypeStore.Validate("B789-X_1.2", "787-9", null));
        }

        /// <summary>長すぎる入力は弾く</summary>
        [TestMethod]
        public void RejectsTooLongValue()
        {
            Assert.AreEqual($"型式コードが長すぎます。{TypeStore.MAX_CODE_LENGTH}文字までです。",
                TypeStore.Validate(new string('A', TypeStore.MAX_CODE_LENGTH + 1), "787-9", null));

            Assert.AreEqual($"型式名が長すぎます。{TypeStore.MAX_NAME_LENGTH}文字までです。",
                TypeStore.Validate("B789", new string('あ', TypeStore.MAX_NAME_LENGTH + 1), null));

            Assert.AreEqual($"表示順が長すぎます。{TypeStore.MAX_DISPLAY_ORDER_LENGTH}文字までです。",
                TypeStore.Validate("B789", "787-9", new string('0', TypeStore.MAX_DISPLAY_ORDER_LENGTH + 1)));
        }
    }
}
