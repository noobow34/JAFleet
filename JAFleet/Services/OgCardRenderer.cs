using JAFleet.Commons.Constants;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JAFleet.Services
{
    /// <summary>カード画像に載せる内容</summary>
    public record OgCardData(string Registration, string? Type, string? Airline, string? Operation, string? OperationCode, string? SpecialLivery);

    /// <summary>
    /// 機体詳細の OGP 用のカード画像（1200×630 の PNG）を描く。
    /// 他人の写真は OGP に使えない（撮影者の表記もリンクも付けられないため）ので、レジ・型式・航空会社などの文字だけで作る。
    /// 描画は ImageSharp（純粋なマネージドコードなのでサーバーに追加のライブラリは要らない）、
    /// 日本語は同梱の Noto Sans JP Bold（SIL OFL 1.1、Fonts/ 以下）で描く。
    /// </summary>
    public class OgCardRenderer
    {
        public const int WIDTH = 1200;
        public const int HEIGHT = 630;
        private const float MARGIN = 64;
        private const float FOOTER_HEIGHT = 72;

        private static readonly Color Ink = Color.ParseHex("0f172a");
        private static readonly Color Sub = Color.ParseHex("334155");
        private static readonly Color Muted = Color.ParseHex("64748b");
        private static readonly Color Livery = Color.ParseHex("dc2626");

        private readonly FontFamily _family;
        private readonly Image<Rgba32>? _logo;

        /// <param name="fontPath">Noto Sans JP Bold のファイル</param>
        /// <param name="logoPath">ロゴ（JA-Fleet_1.png）。無ければロゴなしで描く</param>
        public OgCardRenderer(string fontPath, string? logoPath)
        {
            var fonts = new FontCollection();
            _family = fonts.Add(fontPath);
            if (logoPath != null && File.Exists(logoPath))
            {
                _logo = Image.Load<Rgba32>(logoPath);
                //ロゴは高さ 88px に縮めておく（毎回縮めない）
                _logo.Mutate(x => x.Resize(0, 88));
            }
        }

        public byte[] Render(OgCardData d)
        {
            using var img = new Image<Rgba32>(WIDTH, HEIGHT, Color.White);
            img.Mutate(ctx =>
            {
                //上の細い帯（ロゴの赤と青）
                ctx.Fill(Color.ParseHex("e60012"), new RectangleF(0, 0, WIDTH * 0.62f, 10));
                ctx.Fill(Color.ParseHex("1d8fd8"), new RectangleF(WIDTH * 0.62f, 0, WIDTH * 0.38f, 10));

                if (_logo != null)
                {
                    ctx.DrawImage(_logo, new Point((int)MARGIN, 44), 1f);
                }

                //運用状況のバッジ（右上）
                if (!string.IsNullOrWhiteSpace(d.Operation))
                {
                    var badgeFont = _family.CreateFont(34);
                    var size = TextMeasurer.MeasureSize(d.Operation, new TextOptions(badgeFont));
                    float w = size.Width + 48, h = 64;
                    float x = WIDTH - MARGIN - w, y = 56;
                    ctx.Fill(BadgeColor(d.OperationCode), new RectangleF(x, y, w, h));
                    ctx.DrawText(d.Operation, badgeFont, Color.White, new PointF(x + 24, y + (h - size.Height) / 2 - 4));
                }

                //レジ（いちばん大きく）
                float regSize = FitSize(d.Registration, 210, WIDTH - MARGIN * 2);
                ctx.DrawText(d.Registration, _family.CreateFont(regSize), Ink, new PointF(MARGIN, 150));

                //型式・航空会社・特別塗装。特別塗装があるときは1行増えるので、全体を少し詰める
                bool hasLivery = !string.IsNullOrWhiteSpace(d.SpecialLivery);
                float typeY = hasLivery ? 380 : 395;
                float airlineY = hasLivery ? 452 : 480;
                if (!string.IsNullOrWhiteSpace(d.Type))
                {
                    float typeSize = FitSize(d.Type, hasLivery ? 58 : 64, WIDTH - MARGIN * 2);
                    ctx.DrawText(d.Type, _family.CreateFont(typeSize), Sub, new PointF(MARGIN, typeY));
                }
                if (!string.IsNullOrWhiteSpace(d.Airline))
                {
                    float airlineSize = FitSize(d.Airline, hasLivery ? 40 : 44, WIDTH - MARGIN * 2);
                    ctx.DrawText(d.Airline, _family.CreateFont(airlineSize), Muted, new PointF(MARGIN, airlineY));
                }
                if (hasLivery)
                {
                    var liveryFont = _family.CreateFont(34);
                    string text = Ellipsis("◎ 特別塗装：" + d.SpecialLivery!.Trim(), liveryFont, WIDTH - MARGIN * 2);
                    ctx.DrawText(text, liveryFont, Livery, new PointF(MARGIN, 506));
                }

                //下の帯
                ctx.Fill(Ink, new RectangleF(0, HEIGHT - FOOTER_HEIGHT, WIDTH, FOOTER_HEIGHT));
                var footFont = _family.CreateFont(28);
                ctx.DrawText("本邦航空会社機材一覧サイト", footFont, Color.ParseHex("cbd5e1"), new PointF(MARGIN, HEIGHT - FOOTER_HEIGHT + 18));
                const string url = "ja-fleet.noobow.me";
                float urlWidth = TextMeasurer.MeasureSize(url, new TextOptions(footFont)).Width;
                ctx.DrawText(url, footFont, Color.White, new PointF(WIDTH - MARGIN - urlWidth, HEIGHT - FOOTER_HEIGHT + 18));
            });
            using var ms = new MemoryStream();
            img.SaveAsPng(ms);
            return ms.ToArray();
        }

        /// <summary>運用状況の色。運用中は青、退役は灰、受領前は橙、それ以外は紺</summary>
        public static Color BadgeColor(string? operationCode)
        {
            if (operationCode != null && OperationCode.IN_OPERATION.Contains(operationCode))
            {
                return Color.ParseHex("1d4ed8");
            }
            if (operationCode != null && OperationCode.RETIRE.Contains(operationCode))
            {
                return Color.ParseHex("64748b");
            }
            if (operationCode != null && OperationCode.PRE_OPERATION.Contains(operationCode))
            {
                return Color.ParseHex("d97706");
            }
            return Color.ParseHex("334155");
        }

        /// <summary>幅に収まるまで文字の大きさを下げる</summary>
        private float FitSize(string text, float max, float width)
        {
            float size = max;
            while (size > 20 && TextMeasurer.MeasureSize(text, new TextOptions(_family.CreateFont(size))).Width > width)
            {
                size -= 4;
            }
            return size;
        }

        /// <summary>幅に収まらなければ末尾を「…」にする</summary>
        private static string Ellipsis(string text, Font font, float width)
        {
            if (width <= 0)
            {
                return string.Empty;
            }
            if (TextMeasurer.MeasureSize(text, new TextOptions(font)).Width <= width)
            {
                return text;
            }
            for (int len = text.Length - 1; len > 0; len--)
            {
                string t = text[..len] + "…";
                if (TextMeasurer.MeasureSize(t, new TextOptions(font)).Width <= width)
                {
                    return t;
                }
            }
            return string.Empty;
        }
    }
}
