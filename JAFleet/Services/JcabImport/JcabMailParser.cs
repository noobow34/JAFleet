using System.Text;
using System.Text.RegularExpressions;

namespace JAFleet.Services.JcabImport
{
    /// <summary>
    /// 航空局から届くメール本体から分かることを拾う。
    ///
    /// 件名: 【国土交通省航空局 総務課】航空機登録情報　2026年8月分
    ///       → 対象月。Excelを開く前に一覧へ出せる。再送には (再送) や【再送】が付く
    /// 本文: Tel: 03-5253-8111（内線48146） / hqt-register48146@gxb.mlit.go.jp
    ///       → パスワードに使う内線番号。担当官が異動すると変わるため、
    ///         codeテーブルの登録値が外れていてもここから拾えるようにしておく
    /// </summary>
    public static partial class JcabMailParser
    {
        [GeneratedRegex(@"(\d{4})年\s*(\d{1,2})月分")]
        private static partial Regex TargetMonthRegex();

        [GeneratedRegex(@"内線\s*[:：]?\s*(\d{3,6})")]
        private static partial Regex ExtensionRegex();

        /// <summary>問い合わせ先アドレスにも内線番号が入っている（register48146@）</summary>
        [GeneratedRegex(@"register(\d{3,6})@")]
        private static partial Regex MailAddressExtensionRegex();

        /// <summary>件名から対象月（yyyy/MM）を読む。読めなければnull。</summary>
        public static string? ParseTargetMonth(string? subject)
        {
            Match m = TargetMonthRegex().Match(Normalize(subject));
            return m.Success ? $"{int.Parse(m.Groups[1].Value):D4}/{int.Parse(m.Groups[2].Value):D2}" : null;
        }

        /// <summary>再送メールか。再送分のパスワードは元のメールの送信年月日のままになる。</summary>
        public static bool IsResend(string? subject)
            => Normalize(subject).Contains("再送");

        /// <summary>
        /// 本文から内線番号の候補を拾う。署名の「内線」表記を先に、問い合わせ先アドレスを後に置く。
        /// 重複は除く。
        /// </summary>
        public static List<string> ParseExtensions(string? body)
        {
            string text = Normalize(body);
            List<string> extensions = [];

            foreach (Regex regex in new[] { ExtensionRegex(), MailAddressExtensionRegex() })
            {
                foreach (Match m in regex.Matches(text))
                {
                    string value = m.Groups[1].Value;
                    if (!extensions.Contains(value))
                    {
                        extensions.Add(value);
                    }
                }
            }
            return extensions;
        }

        /// <summary>全角数字や全角スペースで書かれていても拾えるように幅を揃える</summary>
        private static string Normalize(string? value)
            => string.IsNullOrEmpty(value) ? string.Empty : value.Normalize(NormalizationForm.FormKC);
    }
}
