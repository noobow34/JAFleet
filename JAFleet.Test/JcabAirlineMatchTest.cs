using JAFleet.Commons.Data;
using JAFleet.Services.JcabImport;

namespace JAFleet.Test
{
    /// <summary>
    /// 航空局Excel取込で、所有者名から航空会社を推定する判定。
    /// マスタの名称は開発DBの航空会社マスタに入っている値をそのまま写している。
    /// </summary>
    [TestClass]
    public sealed class JcabAirlineMatchTest
    {
        private static readonly List<(string Code, string Name, string Normalized)> NAMES =
            ImportPreviewBuilder.BuildAirlineNames(
            [
                new Airline { AirlineCode = "ANA", AirlineNameJp = "ANA", AirlineNameJpShort = "ANA", AirlineNameEn = "All Nippon Airways", AirlineNameEnShort = "全日本空輸" },
                new Airline { AirlineCode = "AKX", AirlineNameJp = "ANA Wings", AirlineNameJpShort = "ANAウイングス", AirlineNameEn = "ANA Wings", AirlineNameEnShort = "ANAウイングス" },
                new Airline { AirlineCode = "JAL", AirlineNameJp = "JAL", AirlineNameJpShort = "JAL", AirlineNameEn = "Japan Airline", AirlineNameEnShort = "日本航空" },
                new Airline { AirlineCode = "ADO", AirlineNameJp = "AirDo", AirlineNameJpShort = "エア・ドゥ", AirlineNameEn = "AirDo", AirlineNameEnShort = "エア・ドゥ" },
                new Airline { AirlineCode = "J-AIR", AirlineNameJp = "J-AIR", AirlineNameJpShort = "J-AIR", AirlineNameEn = "J-AIR", AirlineNameEnShort = "ジェイエア" },
                new Airline { AirlineCode = "CUK", AirlineNameJp = "CUK", AirlineNameJpShort = "新中央航空", AirlineNameEn = "New Central Airservice", AirlineNameEnShort = "新中央航空" },
            ]);

        private static string? Code(string? owner) => ImportPreviewBuilder.MatchAirline(owner, NAMES).Code;

        [TestMethod]
        public void AirlineAtTheStartIsMatched()
        {
            Assert.AreEqual("JAL", Code("日本航空株式会社"));
            Assert.AreEqual("ANA", Code("ANAホールディングス株式会社"));
            Assert.AreEqual("ANA", Code("全日本空輸株式会社"));
        }

        [TestMethod]
        public void CorporateFormBeforeTheNameIsIgnored()
        {
            Assert.AreEqual("ADO", Code("株式会社エア・ドゥ"));
            Assert.AreEqual("CUK", Code("新中央航空株式会社"));
        }

        [TestMethod]
        public void LongestNameWins()
        {
            //ANA と ANAウイングス の両方が先頭から当たるが、長い方を採る
            Assert.AreEqual("AKX", Code("株式会社ANAウイングス"));
        }

        [TestMethod]
        public void NameInsideAnotherCompanyIsNotMatched()
        {
            //2026年10月分のJA848N。「中日本航空」の中に「日本航空」が含まれていた
            Assert.IsNull(Code("中日本航空株式会社"));
            Assert.IsNull(Code("西日本空輸株式会社"));
            //3文字の名称（ANA）が英語の社名の途中に現れても拾わない
            Assert.IsNull(Code("CANADA AVIATION LTD."));
        }

        [TestMethod]
        public void UnrelatedOwnersAreNotMatched()
        {
            //「ジェイ」までは同じでも別の会社
            Assert.IsNull(Code("株式会社ジェイキャスエアウェイズ"));
            Assert.IsNull(Code("個人"));
            Assert.IsNull(Code(null));
        }

        [TestMethod]
        public void OwnerCoreStripsCorporateFormOnBothSides()
        {
            Assert.AreEqual("日本航空", ImportPreviewBuilder.OwnerCore("日本航空株式会社"));
            Assert.AreEqual("ジェイキャスエアウェイズ", ImportPreviewBuilder.OwnerCore("株式会社ジェイキャスエアウェイズ"));
            Assert.AreEqual("ヒラタ学園", ImportPreviewBuilder.OwnerCore("学校法人ヒラタ学園"));
        }
    }
}
