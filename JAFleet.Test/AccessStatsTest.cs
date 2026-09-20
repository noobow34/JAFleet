using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using JAFleet.Services;

namespace JAFleet.Test
{
    /// <summary>
    /// アクセス集計の分類まわり。集計そのものはDBのGROUP BYなので、
    /// ここではアプリ側で判断している「ボットか」「どの端末か」「どのページか」を確かめる。
    /// </summary>
    [TestClass]
    public sealed class AccessStatsTest
    {
        [TestMethod]
        public void BotLikeUserAgentIsDetected()
        {
            Assert.IsTrue(UserAgentUtil.IsBotLike("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)"));
            Assert.IsTrue(UserAgentUtil.IsBotLike("Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)"));
            Assert.IsTrue(UserAgentUtil.IsBotLike("python-requests/2.31.0"));
            Assert.IsTrue(UserAgentUtil.IsBotLike("curl/8.4.0"));
            Assert.IsTrue(UserAgentUtil.IsBotLike("facebookexternalhit/1.1"));
            //User-Agentを名乗らないクライアントもボット扱いにする
            Assert.IsTrue(UserAgentUtil.IsBotLike(null));
            Assert.IsTrue(UserAgentUtil.IsBotLike(""));
        }

        [TestMethod]
        public void BrowserUserAgentIsNotBot()
        {
            Assert.IsFalse(UserAgentUtil.IsBotLike("Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1"));
            Assert.IsFalse(UserAgentUtil.IsBotLike("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"));
            Assert.IsFalse(UserAgentUtil.IsBotLike("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15"));
        }

        /// <summary>
        /// 端末の判定はEFがSQLへ翻訳できるよう式として持っている。
        /// Compileすれば手元でも同じ判定になるので、そこを突いて確かめる
        /// </summary>
        [TestMethod]
        public void DeviceKeyClassifiesUserAgent()
        {
            Func<AccessLog, string> device = UserAgentUtil.DeviceKey.Compile();

            Assert.AreEqual("スマホ", device(new AccessLog { UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) Mobile/15E148 Safari/604.1" }));
            Assert.AreEqual("スマホ", device(new AccessLog { UserAgent = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 Chrome/126.0.0.0 Mobile Safari/537.36" }));
            Assert.AreEqual("タブレット", device(new AccessLog { UserAgent = "Mozilla/5.0 (iPad; CPU OS 17_5 like Mac OS X) Version/17.5 Safari/604.1" }));
            //AndroidでMobileが付かないものはタブレット
            Assert.AreEqual("タブレット", device(new AccessLog { UserAgent = "Mozilla/5.0 (Linux; Android 14; SM-X200) AppleWebKit/537.36 Chrome/126.0.0.0 Safari/537.36" }));
            Assert.AreEqual("PC", device(new AccessLog { UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/126.0.0.0 Safari/537.36" }));
            Assert.AreEqual("PC", device(new AccessLog { UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Version/17.5 Safari/605.1.15" }));
            Assert.AreEqual("不明", device(new AccessLog { UserAgent = null }));
        }

        /// <summary>静的ファイル（route_keyがnull）とAPI・AJAXはページとして数えないこと</summary>
        [TestMethod]
        public void IsPageExcludesAssetsAndApi()
        {
            Assert.IsTrue(AccessPath.IsPage("Home/Index"));
            Assert.IsTrue(AccessPath.IsPage("AircraftDetail/IndexNohead"));
            Assert.IsTrue(AccessPath.IsPage("Search/Index"));

            //ルートに当たらなかったもの（静的ファイル、存在しないパス）
            Assert.IsFalse(AccessPath.IsPage(null));
            Assert.IsFalse(AccessPath.IsPage(""));
            //  /api 配下のJSON API
            Assert.IsFalse(AccessPath.IsPage("Reg/Get"));
            Assert.IsFalse(AccessPath.IsPage("AirlineGroup/Get"));
            //画面から叩くAJAXと、押すだけで画面を返さないもの
            Assert.IsFalse(AccessPath.IsPage("Master/GetAllReg"));
            Assert.IsFalse(AccessPath.IsPage("Batch/RefreshPhoto"));
            Assert.IsFalse(AccessPath.IsPage("Check/Index"));
        }

        /// <summary>
        /// 除外は「コントローラー名が丸ごと一致」で判定すること。
        /// 前方一致で見ているとAirlineGroupまで巻き込む
        /// </summary>
        [TestMethod]
        public void ControllerNameIsMatchedWhole()
        {
            Assert.AreEqual("Airline", AccessPath.Controller("Airline/Get"));
            Assert.AreEqual("AirlineGroup", AccessPath.Controller("AirlineGroup/Get"));
            Assert.AreEqual("Home", AccessPath.Controller("Home"));
            Assert.AreEqual(string.Empty, AccessPath.Controller(null));
        }

        [TestMethod]
        public void DisplayNameIsJapaneseForKnownControllers()
        {
            Assert.AreEqual("トップ", AccessPath.DisplayName("Home/Index"));
            Assert.AreEqual("機体詳細", AccessPath.DisplayName("AircraftDetail/Index"));
            //派生アクションも同じ呼び名にまとまること
            Assert.AreEqual("機体詳細", AccessPath.DisplayName("AircraftDetail/Emb"));
            Assert.AreEqual("機体一覧", AccessPath.DisplayName("Aircraft/AirlineGroup"));
            //知らないコントローラーは名前をそのまま出す
            Assert.AreEqual("Unknown", AccessPath.DisplayName("Unknown/Index"));
            Assert.AreEqual("その他", AccessPath.DisplayName(null));
        }

        /// <summary>
        /// 移行措置。route_keyが無い古い行はパスからコントローラーの呼び名を当てる。
        /// ルーティングの別名を吸収して、route_keyがある行と同じ名前に寄せること
        /// </summary>
        [TestMethod]
        public void LegacyDisplayNameMatchesRouteKeyNaming()
        {
            Assert.AreEqual(AccessPath.DisplayName("Home/Index"), AccessPath.LegacyDisplayName("/"));
            Assert.AreEqual(AccessPath.DisplayName("AircraftDetail/Index"), AccessPath.LegacyDisplayName("/AD/JA226P"));
            Assert.AreEqual(AccessPath.DisplayName("AircraftDetail/Index"), AccessPath.LegacyDisplayName("/ADNB/JA226P"));
            Assert.AreEqual(AccessPath.DisplayName("AircraftDetail/Index"), AccessPath.LegacyDisplayName("/AircraftDetail/JA226P"));
            Assert.AreEqual(AccessPath.DisplayName("Aircraft/AirlineGroup"), AccessPath.LegacyDisplayName("/Aircraft/AirlineGroup/1"));
            Assert.AreEqual(AccessPath.DisplayName("Search/Index"), AccessPath.LegacyDisplayName("/Search"));
            //ルーティング上の別名も同じコントローラーへ寄せる
            Assert.AreEqual(AccessPath.DisplayName("Log/Index"), AccessPath.LegacyDisplayName("/logy"));
            //大文字小文字は問わない
            Assert.AreEqual(AccessPath.DisplayName("Search/Index"), AccessPath.LegacyDisplayName("/search"));
            //知らないパスは先頭セグメントをそのまま出す
            Assert.AreEqual("unknown", AccessPath.LegacyDisplayName("/unknown/x"));
        }

        [TestMethod]
        public void DetailRegistrationMergesVariants()
        {
            Assert.AreEqual("JA226P", AccessPath.DetailRegistration("/AD/JA226P"));
            Assert.AreEqual("JA226P", AccessPath.DetailRegistration("/ADN/JA226P"));
            Assert.AreEqual("JA226P", AccessPath.DetailRegistration("/ADNB/JA226P"));
            Assert.AreEqual("JA226P", AccessPath.DetailRegistration("/ADE/JA226P"));
            Assert.AreEqual("JA226P", AccessPath.DetailRegistration("/AircraftDetail/JA226P"));
            //大文字小文字が違っても同じ1機として数える
            Assert.AreEqual("JA226P", AccessPath.DetailRegistration("/ad/ja226p"));
            //レジが無い、機体詳細ではない
            Assert.IsNull(AccessPath.DetailRegistration("/AD"));
            Assert.IsNull(AccessPath.DetailRegistration("/Admin"));
            Assert.IsNull(AccessPath.DetailRegistration("/Search"));
        }
    }
}
