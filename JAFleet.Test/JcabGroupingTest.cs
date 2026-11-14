using JAFleet.Services.JcabImport;

namespace JAFleet.Test
{
    /// <summary>
    /// 航空局Excel取込のプレビューで、同じグループ（同じ機材のまとまり）と見なす行の判定。
    /// レジ・型式・所有者は2026年10月分のExcelに実際に載っていた値。
    /// </summary>
    [TestClass]
    public sealed class JcabGroupingTest
    {
        private static ImportPreviewItem Item(string reg, string? type, string? owner) => new()
        {
            RegistrationNumber = reg,
            TypeDisplay = type,
            Owner = owner,
        };

        [TestMethod]
        public void RegistrationShapeReplacesDigitsOnly()
        {
            Assert.AreEqual("JA##EA", ImportPreviewBuilder.RegistrationShape("JA01EA"));
            Assert.AreEqual("JA###J", ImportPreviewBuilder.RegistrationShape("JA892J"));
            Assert.AreEqual("JA####", ImportPreviewBuilder.RegistrationShape("JA4046"));
        }

        [TestMethod]
        public void SameShapeTypeAndOwnerFormOneGroup()
        {
            List<ImportPreviewItem> items =
            [
                Item("JA01EA", "エンブラエル E190-E2", "ANAホールディングス株式会社"),
                Item("JA02EA", "エンブラエル E190-E2", "ANAホールディングス株式会社"),
                Item("JA29EA", "エンブラエル E190-E2", "ANAホールディングス株式会社"),
            ];

            ImportPreviewBuilder.AssignGroups(items);

            Assert.IsNotNull(items[0].GroupKey);
            Assert.IsTrue(items.All(i => i.GroupKey == items[0].GroupKey));
            Assert.IsTrue(items.All(i => i.GroupLabel == "JA##EA" && i.GroupSize == 3));
        }

        [TestMethod]
        public void SameShapeWithDifferentTypeAndOwnerIsNotGrouped()
        {
            //JA##XJ に、JALのA350-900予約と、リース会社へのA350-941の移転が混ざっていた
            List<ImportPreviewItem> items =
            [
                Item("JA14XJ", "ｴｱﾊﾞｽ A350-941", "ダイヤウィザード有限会社"),
                Item("JA51XJ", "エアバス 350-900", "日本航空株式会社"),
                Item("JA52XJ", "エアバス 350-900", "日本航空株式会社"),
            ];

            ImportPreviewBuilder.AssignGroups(items);

            Assert.IsNull(items[0].GroupKey);
            Assert.IsNotNull(items[1].GroupKey);
            Assert.AreEqual(items[1].GroupKey, items[2].GroupKey);
            Assert.AreEqual(2, items[1].GroupSize);
        }

        [TestMethod]
        public void DifferentOwnerIsNotGrouped()
        {
            List<ImportPreviewItem> items =
            [
                Item("JA01EA", "エンブラエル E190-E2", "ANAホールディングス株式会社"),
                Item("JA02EA", "エンブラエル E190-E2", "全日本空輸株式会社"),
            ];

            ImportPreviewBuilder.AssignGroups(items);

            Assert.IsTrue(items.All(i => i.GroupKey == null));
        }

        [TestMethod]
        public void SpellingVariationsOfTypeAreAbsorbed()
        {
            //同じ月のExcelの中で、中黒（･と・）と半角／全角のズが揺れていた
            List<ImportPreviewItem> items =
            [
                Item("JA005F", "ｴｱﾊﾞｽ･ﾍﾘｺﾌﾟﾀｰｽﾞ BK117D-3", "川重岐阜サービス株式会社"),
                Item("JA145F", "ｴｱﾊﾞｽ・ﾍﾘｺﾌﾟﾀｰズ BK117D-3", "川重岐阜サービス株式会社"),
            ];

            ImportPreviewBuilder.AssignGroups(items);

            Assert.IsNotNull(items[0].GroupKey);
            Assert.AreEqual(items[0].GroupKey, items[1].GroupKey);
        }

        [TestMethod]
        public void SingleRowAndRowsWithoutTypeAreNotGrouped()
        {
            List<ImportPreviewItem> items =
            [
                Item("JA848N", "テキストロン・アビエーション 208", "中日本航空株式会社"),
                //変更登録だけの行は型式が読めない
                Item("JA27MC", null, null),
                Item("JA28MC", null, null),
            ];

            ImportPreviewBuilder.AssignGroups(items);

            Assert.IsTrue(items.All(i => i.GroupKey == null));
        }
    }
}
