using JAFleet.Services.Livery;

namespace JAFleet.Test
{
    /// <summary>
    /// 特別塗装ニュースの判定で、外から来たものを読む部分。
    /// RSSはAviation Wireのものを、GeminiのJSONは返させている形を写している。
    /// </summary>
    [TestClass]
    public sealed class LiveryNewsTest
    {
        private const string RSS = """
            <?xml version="1.0" encoding="UTF-8"?><rss version="2.0" xmlns:dc="http://purl.org/dc/elements/1.1/">
            <channel>
              <title>Aviation Wire</title>
              <link>https://www.aviationwire.jp</link>
              <item>
                <title>ANA、ポケモンジェット就航</title>
                <link>https://www.aviationwire.jp/archives/354464</link>
                <dc:creator><![CDATA[Tadayuki YOSHIKAWA]]></dc:creator>
                <pubDate>Thu, 12 Nov 2026 22:37:46 +0000</pubDate>
                <category><![CDATA[エアライン]]></category>
                <guid isPermaLink="false">https://www.aviationwire.jp/?p=354464</guid>
                <description><![CDATA[　全日本空輸（ANA/NH）は、ボーイング787-9型機（JA926A）に特別塗装を施した [&#8230;]]]></description>
              </item>
              <item>
                <title>日本空港ビル、株式を3分割</title>
                <link>https://www.aviationwire.jp/archives/354454</link>
                <pubDate>Thu, 12 Nov 2026 21:30:27 +0000</pubDate>
                <description><![CDATA[　羽田空港の旅客ターミナルを運営する日本空港ビルデング（9706）は、普通株式1株を3株に分割する。]]></description>
              </item>
            </channel>
            </rss>
            """;

        [TestMethod]
        public void RssIsParsedNewestFirst()
        {
            var items = AviationWireRss.Parse(RSS);

            Assert.AreEqual(2, items.Count);
            Assert.AreEqual("ANA、ポケモンジェット就航", items[0].Title);
            Assert.AreEqual("https://www.aviationwire.jp/archives/354464", items[0].Url);
            Assert.AreEqual("日本空港ビル、株式を3分割", items[1].Title);
        }

        /// <summary>pubDateはUTCで書かれているので、こちらの時刻に直して持つ</summary>
        [TestMethod]
        public void PubDateIsLocalTime()
        {
            var items = AviationWireRss.Parse(RSS);

            DateTime expected = new DateTimeOffset(2026, 11, 12, 22, 37, 46, TimeSpan.Zero).ToLocalTime().DateTime;
            Assert.AreEqual(expected, items[0].PublishedAt);
        }

        /// <summary>抜粋の末尾に付く「[…]」は判定の邪魔になるので落とす</summary>
        [TestMethod]
        public void SummaryDropsEllipsis()
        {
            var items = AviationWireRss.Parse(RSS);

            Assert.IsNotNull(items[0].Summary);
            Assert.IsFalse(items[0].Summary!.Contains('…'));
            Assert.IsTrue(items[0].Summary!.EndsWith("特別塗装を施した"));
        }

        /// <summary>linkが無い項目は記事として扱えないので飛ばす</summary>
        [TestMethod]
        public void ItemWithoutLinkIsSkipped()
        {
            var items = AviationWireRss.Parse("""
                <rss version="2.0"><channel>
                  <item><title>リンクなし</title></item>
                  <item><title>あり</title><link>https://www.aviationwire.jp/archives/1</link></item>
                </channel></rss>
                """);

            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("あり", items[0].Title);
        }

        /// <summary>
        /// Aviation WireはUser-Agentが空だと403を返す。.NETのHttpClientは既定で送らないので、
        /// 付け忘れるとRSSが丸ごと取れなくなる。
        /// </summary>
        [TestMethod]
        public void RequestHasUserAgent()
        {
            using var request = AviationWireRss.BuildRequest();

            Assert.AreEqual(LiveryNewsConstant.RSS_URL, request.RequestUri!.ToString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(request.Headers.UserAgent.ToString()), "User-Agentが空");
        }

        [TestMethod]
        public void RegistrationIsNormalized()
        {
            //小文字・全角・区切り入りで書かれていても同じレジとして扱う
            Assert.AreEqual("JA926A", LiveryNewsChecker.NormalizeRegistration("ja926a"));
            Assert.AreEqual("JA926A", LiveryNewsChecker.NormalizeRegistration(" JA-926A "));
            Assert.AreEqual("JA926A", LiveryNewsChecker.NormalizeRegistration("ＪＡ９２６Ａ"));
        }

        [TestMethod]
        public void RegistrationRejectsNonJapanese()
        {
            //JAで始まらないものは日本の機体ではない
            Assert.IsNull(LiveryNewsChecker.NormalizeRegistration("N12345"));
            //短すぎる・長すぎるものは読み間違い
            Assert.IsNull(LiveryNewsChecker.NormalizeRegistration("JA12"));
            Assert.IsNull(LiveryNewsChecker.NormalizeRegistration("JA926A-787-9"));
            Assert.IsNull(LiveryNewsChecker.NormalizeRegistration(null));
            Assert.IsNull(LiveryNewsChecker.NormalizeRegistration("  "));
        }

        [TestMethod]
        public void GeminiResponseIsDeserialized()
        {
            var result = GeminiLiveryJudge.Deserialize("""
                {
                  "special_livery": true,
                  "reason": "787-9に特別塗装を施したという記事のため。",
                  "targets": [
                    { "registration": "JA926A", "livery_name": "ポケモンジェット" },
                    { "registration": null, "livery_name": "ポケモンジェット" }
                  ]
                }
                """);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.SpecialLivery);
            Assert.AreEqual("787-9に特別塗装を施したという記事のため。", result.Reason);
            Assert.AreEqual(2, result.Targets!.Count);
            Assert.AreEqual("JA926A", result.Targets[0].Registration);
            Assert.AreEqual("ポケモンジェット", result.Targets[0].LiveryName);
            Assert.IsNull(result.Targets[1].Registration);
        }

        /// <summary>JSONだけを返させているが、コードブロックで包まれてくることがある</summary>
        [TestMethod]
        public void GeminiResponseInCodeBlockIsDeserialized()
        {
            var result = GeminiLiveryJudge.Deserialize("""
                ```json
                { "special_livery": false, "reason": "決算の記事のため。", "targets": [] }
                ```
                """);

            Assert.IsNotNull(result);
            Assert.IsFalse(result.SpecialLivery);
            Assert.AreEqual(0, result.Targets!.Count);
        }

        /// <summary>対象機が挙がっているのにfalseで返ってくることがあるので揃える</summary>
        [TestMethod]
        public void SpecialLiveryIsCorrectedWhenTargetsExist()
        {
            var result = GeminiLiveryJudge.Deserialize("""
                { "special_livery": false, "targets": [ { "registration": "JA926A", "livery_name": "ポケモンジェット" } ] }
                """);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.SpecialLivery);
        }

        [TestMethod]
        public void BrokenGeminiResponseIsNull()
        {
            Assert.IsNull(GeminiLiveryJudge.Deserialize("判定できませんでした"));
        }

        /// <summary>プロンプトには記事そのものの情報が入っていること（URLを読ませる前提のため）</summary>
        [TestMethod]
        public void PromptContainsArticle()
        {
            string prompt = GeminiLiveryJudge.BuildPrompt(
                new RssItem("https://www.aviationwire.jp/archives/354464", "ANA、ポケモンジェット就航", null, "概要です"));

            StringAssert.Contains(prompt, "https://www.aviationwire.jp/archives/354464");
            StringAssert.Contains(prompt, "ANA、ポケモンジェット就航");
            StringAssert.Contains(prompt, "概要です");
            StringAssert.Contains(prompt, "\"special_livery\"");
            StringAssert.Contains(prompt, "URLの内容を読んでから");
        }

        /// <summary>お試し判定で本文だけを貼った場合は、URLを読ませる指示を出さない</summary>
        [TestMethod]
        public void PromptWithoutUrlUsesPastedBody()
        {
            string prompt = GeminiLiveryJudge.BuildPrompt(
                new RssItem("", "", null, "全日本空輸は、787-9（JA926A）に特別塗装を施した。"));

            StringAssert.Contains(prompt, "全日本空輸は、787-9（JA926A）に特別塗装を施した。");
            StringAssert.Contains(prompt, "下に貼ってある");
            Assert.IsFalse(prompt.Contains("URL:"), "URLが無いのにURL行が出ている");
            Assert.IsFalse(prompt.Contains("URLの内容を読んでから"));
        }

        /// <summary>お試し判定の入力の検査。DBを見ないで分かる範囲</summary>
        [TestMethod]
        public void TrialValidatesInput()
        {
            //URLも本文も無ければ判定するものが無い
            Assert.IsNotNull(LiveryNewsTrial.Validate("", null, save: false));
            //本文だけなら判定はできる
            Assert.IsNull(LiveryNewsTrial.Validate("", "本文", save: false));
            //候補を作るなら記事URLが要る（一覧から記事を開けるようにするため）
            Assert.IsNotNull(LiveryNewsTrial.Validate("", "本文", save: true));
            //httpで始まらないURLは受けない（手動で作った候補と見分けが付かなくなる）
            Assert.IsNotNull(LiveryNewsTrial.Validate("manual:20261113", null, save: true));
            Assert.IsNull(LiveryNewsTrial.Validate("https://www.aviationwire.jp/archives/1", null, save: true));
        }
    }
}
