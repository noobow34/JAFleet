using JAFleet.Services;

namespace JAFleet.Test
{
    /// <summary>site_settingの値の読み方。行が無い・壊れた値は既定値にする。</summary>
    [TestClass]
    public class SiteSettingsTest
    {
        private const string KEY = SiteSettings.SHOW_NOT_WORKING_INFO_MENU;

        [TestMethod]
        public void 行が無ければ既定値()
        {
            var values = new Dictionary<string, string>();

            Assert.IsTrue(SiteSettings.ReadBool(values, KEY, true));
            Assert.IsFalse(SiteSettings.ReadBool(values, KEY, false));
        }

        [TestMethod]
        public void trueとfalseを読める()
        {
            Assert.IsTrue(SiteSettings.ReadBool(new Dictionary<string, string> { [KEY] = "true" }, KEY, false));
            Assert.IsFalse(SiteSettings.ReadBool(new Dictionary<string, string> { [KEY] = "false" }, KEY, true));
        }

        [TestMethod]
        public void 大文字や前後の空白があっても読める()
        {
            Assert.IsFalse(SiteSettings.ReadBool(new Dictionary<string, string> { [KEY] = " False " }, KEY, true));
            Assert.IsTrue(SiteSettings.ReadBool(new Dictionary<string, string> { [KEY] = "TRUE" }, KEY, false));
        }

        [TestMethod]
        public void 真偽値として読めない値は既定値()
        {
            //手でDBを直して壊した値を入れても、メニューが消えたりはしない
            Assert.IsTrue(SiteSettings.ReadBool(new Dictionary<string, string> { [KEY] = "0" }, KEY, true));
            Assert.IsTrue(SiteSettings.ReadBool(new Dictionary<string, string> { [KEY] = "" }, KEY, true));
        }

        [TestMethod]
        public void 別のキーの値は使わない()
        {
            var values = new Dictionary<string, string> { ["other_setting"] = "false" };

            Assert.IsTrue(SiteSettings.ReadBool(values, KEY, true));
        }
    }
}
