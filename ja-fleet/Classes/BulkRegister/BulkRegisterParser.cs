using System.Text;
using System.Text.RegularExpressions;
using jafleet.Classes.JcabImport;

namespace jafleet.Classes.BulkRegister
{
    /// <summary>
    /// 貼り付けたテキストの1行分。
    /// 解釈できなかった行も画面に出して直してもらうので、Errorを持たせて捨てずに返す。
    /// </summary>
    public sealed class BulkRegisterLine
    {
        /// <summary>入力欄の何行目か。空行も数えるので、画面の表示と一致する。</summary>
        public int LineNumber { get; set; }

        public string RawText { get; set; } = string.Empty;

        public string? RegistrationNumber { get; set; }

        public string? RegisterDate { get; set; }

        /// <summary>
        /// 登録年月日を yyyy/MM/dd に直せず、書いたままを入れる場合にtrue。
        /// 「2026/08/xx」のように日がまだ決まっていない書き方を通すためのもの。
        /// </summary>
        public bool RegisterDateAsIs { get; set; }

        public string? SerialNumber { get; set; }

        public string? Error { get; set; }

        public bool IsValid => Error == null;
    }

    /// <summary>
    /// 同一型式一括登録の入力欄を解釈する。
    /// 1行1機で「レジ / 登録年月日 / 製造番号」。区切りはタブ・カンマ・空白のいずれか。
    /// Excelから列をコピーするとタブ区切りで入るので、そのまま貼れる。
    /// 登録年月日と製造番号は省略できる。
    /// 登録年月日は yyyy/MM/dd に寄せるが、寄せられない書き方はエラーにせず書いたまま通す。
    /// </summary>
    public static partial class BulkRegisterParser
    {
        /// <summary>JAを除いた部分の最短・最長。JcabExcelParserのレジ判定と合わせてある。</summary>
        [GeneratedRegex(@"^JA[0-9A-Z]{2,4}$")]
        private static partial Regex RegistrationRegex();

        /// <summary>入力欄のテキストを1行1機に解釈する。</summary>
        public static List<BulkRegisterLine> Parse(string? text)
        {
            List<BulkRegisterLine> lines = [];
            if (string.IsNullOrWhiteSpace(text))
            {
                return lines;
            }

            //同じレジを2回書いてしまう事故を拾うため、出てきた順に覚えておく
            HashSet<string> seen = new(StringComparer.Ordinal);
            int lineNumber = 0;

            foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                lineNumber++;
                string trimmed = raw.Replace('　', ' ').Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                BulkRegisterLine line = new() { LineNumber = lineNumber, RawText = trimmed };
                lines.Add(line);

                string[] fields = SplitFields(trimmed);
                if (fields.Length > 3)
                {
                    line.Error = "列が多すぎます。レジ・登録年月日・製造番号の3つまでです。";
                    continue;
                }

                line.RegistrationNumber = NormalizeRegistration(fields[0]);
                if (line.RegistrationNumber == null)
                {
                    line.Error = $"レジとして解釈できません（{fields[0]}）。";
                    continue;
                }

                if (!seen.Add(line.RegistrationNumber))
                {
                    line.Error = $"{line.RegistrationNumber} が重複しています。";
                    continue;
                }

                (line.RegisterDate, line.RegisterDateAsIs) = ReadDate(fields.Length > 1 ? fields[1] : null);

                string serial = fields.Length > 2 ? fields[2].Trim() : string.Empty;
                line.SerialNumber = serial.Length == 0 ? null : TextUtil.Widen(serial);
            }

            return lines;
        }

        /// <summary>
        /// 1行を列に割る。
        /// タブやカンマで区切られている場合は空の列も列として残す（登録年月日だけ空のExcel貼り付けを拾うため）。
        /// どちらも無い場合だけ空白区切りとみなす。
        /// </summary>
        private static string[] SplitFields(string line)
        {
            if (line.Contains('\t'))
            {
                return line.Split('\t');
            }
            if (line.Contains(','))
            {
                return line.Split(',');
            }
            return line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        /// <summary>JA付きの大文字レジにする。JAを書かなくても補う。妥当でなければnull。</summary>
        public static string? NormalizeRegistration(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string reg = value.Normalize(NormalizationForm.FormKC)
                              .Replace(" ", string.Empty)
                              .Replace("-", string.Empty)
                              .ToUpperInvariant();

            if (!reg.StartsWith("JA", StringComparison.Ordinal))
            {
                reg = "JA" + reg;
            }

            return RegistrationRegex().IsMatch(reg) ? reg : null;
        }

        /// <summary>
        /// 登録年月日として入れる値を決める。
        /// yyyy/MM/dd に直せた場合はそれを、直せなかった場合は書いたままを返す（AsIs）。
        /// 予定月までしか分からないときの「2026/08/xx」のような書き方をそのまま登録できるようにするため、
        /// 解釈できないことをエラーにはしない。
        /// </summary>
        public static (string? Date, bool AsIs) ReadDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return (null, false);
            }

            string trimmed = value.Trim();
            string? normalized = JcabDateUtil.ToDateString(trimmed);
            return normalized != null ? (normalized, false) : (TextUtil.Widen(trimmed), true);
        }
    }
}
