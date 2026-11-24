using JAFleet.Commons.Constants;
using SkiaSharp;

namespace JAFleet.Services
{
    /// <summary>カード画像に載せる内容</summary>
    public record OgCardData(string Registration, string? Type, string? Airline, string? Operation, string? OperationCode, string? SpecialLivery);

    /// <summary>
    /// 機体詳細の OGP 用のカード画像（1200×630 の PNG）を描く。
    /// 他人の写真は OGP に使えない（撮影者の表記もリンクも付けられないため）ので、レジ・型式・航空会社などの文字だけで作る。
    /// 描画は SkiaSharp（MIT。Linux 用のネイティブライブラリは NuGet の NativeAssets.Linux.NoDependencies に入っている）、
    /// 日本語は同梱の Noto Sans JP Bold（SIL OFL 1.1、Fonts/ 以下）で描く。
    /// 文字はベースラインの y で置く（フォントの ascent は和文の分だけ大きく、上端で揃えると英数字が下がって見えるため）。
    /// </summary>
    public sealed class OgCardRenderer : IDisposable
    {
        public const int WIDTH = 1200;
        public const int HEIGHT = 630;
        private const float MARGIN = 64;
        private const float FOOTER_HEIGHT = 72;

        private static readonly SKColor Ink = SKColor.Parse("0f172a");
        private static readonly SKColor Sub = SKColor.Parse("334155");
        private static readonly SKColor Muted = SKColor.Parse("64748b");
        private static readonly SKColor Livery = SKColor.Parse("dc2626");

        private readonly SKTypeface _typeface;
        private readonly SKImage? _logo;

        /// <param name="fontPath">Noto Sans JP Bold のファイル</param>
        /// <param name="logoPath">ロゴ（JA-Fleet_1.png）。無ければロゴなしで描く</param>
        public OgCardRenderer(string fontPath, string? logoPath)
        {
            _typeface = SKTypeface.FromFile(fontPath)
                ?? throw new FileNotFoundException("OGP のカード画像のフォントを読めません", fontPath);
            if (logoPath != null && File.Exists(logoPath))
            {
                _logo = SKImage.FromEncodedData(logoPath);
            }
        }

        public byte[] Render(OgCardData d)
        {
            using var surface = SKSurface.Create(new SKImageInfo(WIDTH, HEIGHT, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            //上の細い帯（ロゴの赤と青）
            Rect(canvas, SKColor.Parse("e60012"), 0, 0, WIDTH * 0.62f, 10);
            Rect(canvas, SKColor.Parse("1d8fd8"), WIDTH * 0.62f, 0, WIDTH * 0.38f, 10);

            if (_logo != null)
            {
                //高さ 88px に縮めて左上に置く
                float h = 88, w = _logo.Width * h / _logo.Height;
                canvas.DrawImage(_logo, new SKRect(MARGIN, 44, MARGIN + w, 44 + h), new SKSamplingOptions(SKCubicResampler.Mitchell));
            }

            //運用状況のバッジ（右上）
            if (!string.IsNullOrWhiteSpace(d.Operation))
            {
                using var badgeFont = Font(34);
                float textWidth = badgeFont.MeasureText(d.Operation);
                float w = textWidth + 48, h = 64;
                float x = WIDTH - MARGIN - w, y = 56;
                Rect(canvas, BadgeColor(d.OperationCode), x, y, w, h);
                Text(canvas, d.Operation, badgeFont, SKColors.White, x + 24, MiddleBaseline(badgeFont, y, h));
            }

            //レジ（いちばん大きく）
            using (var regFont = Font(FitSize(d.Registration, 210, WIDTH - MARGIN * 2)))
            {
                Text(canvas, d.Registration, regFont, Ink, MARGIN, 345);
            }

            //型式・航空会社・特別塗装。特別塗装があるときは1行増えるので、全体を少し詰める
            bool hasLivery = !string.IsNullOrWhiteSpace(d.SpecialLivery);
            if (!string.IsNullOrWhiteSpace(d.Type))
            {
                using var typeFont = Font(FitSize(d.Type, hasLivery ? 58 : 64, WIDTH - MARGIN * 2));
                Text(canvas, d.Type, typeFont, Sub, MARGIN, hasLivery ? 432 : 452);
            }
            if (!string.IsNullOrWhiteSpace(d.Airline))
            {
                using var airlineFont = Font(FitSize(d.Airline, hasLivery ? 40 : 44, WIDTH - MARGIN * 2));
                Text(canvas, d.Airline, airlineFont, Muted, MARGIN, hasLivery ? 488 : 520);
            }
            if (hasLivery)
            {
                using var liveryFont = Font(34);
                string text = Ellipsis("◎ 特別塗装：" + d.SpecialLivery!.Trim(), liveryFont, WIDTH - MARGIN * 2);
                Text(canvas, text, liveryFont, Livery, MARGIN, 536);
            }

            //下の帯
            Rect(canvas, Ink, 0, HEIGHT - FOOTER_HEIGHT, WIDTH, FOOTER_HEIGHT);
            using (var footFont = Font(28))
            {
                float y = MiddleBaseline(footFont, HEIGHT - FOOTER_HEIGHT, FOOTER_HEIGHT);
                Text(canvas, "本邦航空会社機材一覧サイト", footFont, SKColor.Parse("cbd5e1"), MARGIN, y);
                const string url = "ja-fleet.noobow.me";
                Text(canvas, url, footFont, SKColors.White, WIDTH - MARGIN - footFont.MeasureText(url), y);
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        /// <summary>運用状況の色。運用中は青、退役は灰、受領前は橙、それ以外は紺</summary>
        public static SKColor BadgeColor(string? operationCode)
        {
            if (operationCode != null && OperationCode.IN_OPERATION.Contains(operationCode))
            {
                return SKColor.Parse("1d4ed8");
            }
            if (operationCode != null && OperationCode.RETIRE.Contains(operationCode))
            {
                return SKColor.Parse("64748b");
            }
            if (operationCode != null && OperationCode.PRE_OPERATION.Contains(operationCode))
            {
                return SKColor.Parse("d97706");
            }
            return SKColor.Parse("334155");
        }

        private SKFont Font(float size) => new(_typeface, size) { Subpixel = true, Edging = SKFontEdging.Antialias };

        /// <summary>高さ h の帯（上端 top）の真ん中に文字が来るベースライン。大文字の高さで中央に置く</summary>
        private static float MiddleBaseline(SKFont font, float top, float h)
        {
            float cap = font.Metrics.CapHeight > 0 ? font.Metrics.CapHeight : font.Size * 0.73f;
            return top + (h + cap) / 2;
        }

        /// <summary>ベースラインの y を指定して描く</summary>
        private static void Text(SKCanvas canvas, string text, SKFont font, SKColor color, float x, float baseline)
        {
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, paint);
        }

        private static void Rect(SKCanvas canvas, SKColor color, float x, float y, float w, float h)
        {
            using var paint = new SKPaint { Color = color, IsAntialias = false };
            canvas.DrawRect(x, y, w, h, paint);
        }

        /// <summary>幅に収まるまで文字の大きさを下げる</summary>
        private float FitSize(string text, float max, float width)
        {
            float size = max;
            while (size > 20)
            {
                using var font = Font(size);
                if (font.MeasureText(text) <= width)
                {
                    break;
                }
                size -= 4;
            }
            return size;
        }

        /// <summary>幅に収まらなければ末尾を「…」にする</summary>
        private static string Ellipsis(string text, SKFont font, float width)
        {
            if (width <= 0)
            {
                return string.Empty;
            }
            if (font.MeasureText(text) <= width)
            {
                return text;
            }
            for (int len = text.Length - 1; len > 0; len--)
            {
                string t = text[..len] + "…";
                if (font.MeasureText(t) <= width)
                {
                    return t;
                }
            }
            return string.Empty;
        }

        public void Dispose()
        {
            _logo?.Dispose();
            _typeface.Dispose();
        }
    }
}
