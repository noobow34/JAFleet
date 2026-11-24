using JAFleet.Controllers;
using JAFleet.Services;
using SixLabors.ImageSharp;

namespace JAFleet.Test
{
    [TestClass]
    public sealed class OgCardTest
    {
        //テストはリポジトリの中のフォントとロゴをそのまま使う
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "JAFleet"));
        private static readonly OgCardRenderer Renderer = new(
            Path.Combine(Root, "Fonts", "NotoSansJP-Bold.otf"),
            Path.Combine(Root, "wwwroot", "images", "JA-Fleet_1.png"));

        [TestMethod]
        public void 大きさ1200x630のPNGを描く()
        {
            byte[] png = Renderer.Render(new OgCardData("JA841A", "DHC-8-402", "ANAウイングス", "国内", "4", null));
            using var img = Image.Load(png);
            Assert.AreEqual(OgCardRenderer.WIDTH, img.Width);
            Assert.AreEqual(OgCardRenderer.HEIGHT, img.Height);
        }

        [TestMethod]
        public void 長い型式や特別塗装や空の項目でも落ちない()
        {
            Renderer.Render(new OgCardData("JA01FJ", new string('W', 80), new string('ア', 60), "退役（抹消）", "8", new string('塗', 200)));
            Renderer.Render(new OgCardData("JA000X", null, null, null, null, null));
        }

        [TestMethod]
        public void 中身が変われば署名も変わる()
        {
            var a = new OgCardData("JA841A", "DHC-8-402", "ANAウイングス", "国内", "4", null);
            Assert.AreEqual(OgImageController.Signature(a), OgImageController.Signature(a with { }));
            Assert.AreNotEqual(OgImageController.Signature(a), OgImageController.Signature(a with { SpecialLivery = "特別塗装" }));
            Assert.AreNotEqual(OgImageController.Signature(a), OgImageController.Signature(a with { Operation = "退役（抹消）" }));
            Assert.AreEqual("https://ja-fleet.noobow.me/OgImage/JA841A.png", OgImageController.ImageUrl("JA841A"));
        }
    }
}
