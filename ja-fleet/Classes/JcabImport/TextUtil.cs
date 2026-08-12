using System.Text;

namespace jafleet.Classes.JcabImport
{
    /// <summary>
    /// 航空局Excelは半角カナ・全角英数・全角ハイフンが同一ファイル内で混在するため
    /// 比較する際は必ずここを通して正規化する。
    /// </summary>
    public static class TextUtil
    {
        /// <summary>セルの値を表示用の文字列にする。空セルはnull。</summary>
        public static string? Clean(object? value)
        {
            if (value == null)
            {
                return null;
            }
            string s = value is string str ? str : value.ToString() ?? string.Empty;
            //全角スペースも空白として扱う
            s = s.Replace('　', ' ').Replace("\n", string.Empty).Replace("\r", string.Empty).Trim();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        /// <summary>
        /// 文字幅の揺れだけを揃える。半角カナ→全角カナ、全角英数→半角英数、全角ハイフン→半角。
        /// 比較用のNormalizeと違い大文字化や記号の除去はしないので、そのまま登録値に使える。
        /// </summary>
        public static string? Widen(string? value)
            => string.IsNullOrEmpty(value) ? value : value.Normalize(NormalizationForm.FormKC).Trim();

        /// <summary>
        /// 比較用の正規化。NFKCで半角カナ→全角カナ・全角英数→半角英数・全角ハイフン→半角に寄せ、
        /// 空白と記号の揺れを落として大文字化する。
        /// </summary>
        public static string Normalize(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            string s = value.Normalize(NormalizationForm.FormKC);
            StringBuilder sb = new(s.Length);
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }
                //型式の表記ゆれ（"ｼﾞｭﾆｱ" のダブルクォート、中黒、長音）を落とす
                if (c is '"' or '\'' or '・' or '･' or '.' or '(' or ')')
                {
                    continue;
                }
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }
    }
}
