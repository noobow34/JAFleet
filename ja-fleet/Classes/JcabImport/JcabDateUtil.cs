using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace jafleet.Classes.JcabImport
{
    /// <summary>
    /// 航空局Excelの日付はシートごとに型が違う。
    ///   NEW/TRAN/CNG : Excelのシリアル値（OADate）
    ///   DEL          : "R08年06月02日" の文字列
    ///   Reservation  : "R8.6.3予約"（予約年月日）と "R10.7"（予定年月日、月まで）
    /// jafleetのregister_dateは yyyy/MM/dd の文字列なのでそこに合わせる。
    /// </summary>
    public static partial class JcabDateUtil
    {
        /// <summary>令和元年 = 2019年</summary>
        private const int REIWA_OFFSET = 2018;

        /// <summary>平成元年 = 1989年</summary>
        private const int HEISEI_OFFSET = 1988;

        [GeneratedRegex(@"^R(\d{1,2})[年\.](\d{1,2})(?:[月\.](\d{1,2}))?")]
        private static partial Regex ReiwaRegex();

        [GeneratedRegex(@"^H(\d{1,2})[年\.](\d{1,2})(?:[月\.](\d{1,2}))?")]
        private static partial Regex HeiseiRegex();

        [GeneratedRegex(@"^(\d{4})[/\-\.年](\d{1,2})(?:[/\-\.月](\d{1,2}))?")]
        private static partial Regex SeirekiRegex();

        /// <summary>yyyy/MM/dd を返す。日まで特定できない場合はnull。</summary>
        public static string? ToDateString(object? value)
        {
            (int y, int m, int d)? parsed = Parse(value);
            if (parsed == null || parsed.Value.d == 0)
            {
                return null;
            }
            return $"{parsed.Value.y:D4}/{parsed.Value.m:D2}/{parsed.Value.d:D2}";
        }

        /// <summary>日が無ければ yyyy/MM 、あれば yyyy/MM/dd を返す。納入予定年月日用。</summary>
        public static string? ToLooseDateString(object? value)
        {
            (int y, int m, int d)? parsed = Parse(value);
            if (parsed == null)
            {
                return null;
            }
            return parsed.Value.d == 0
                ? $"{parsed.Value.y:D4}/{parsed.Value.m:D2}"
                : $"{parsed.Value.y:D4}/{parsed.Value.m:D2}/{parsed.Value.d:D2}";
        }

        /// <summary>年月日を返す。日が特定できない場合は d=0。</summary>
        private static (int y, int m, int d)? Parse(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is DateTime dt)
            {
                return (dt.Year, dt.Month, dt.Day);
            }

            //Excelのシリアル値。日付として妥当な範囲のときだけ日付とみなす（製造番号などの数値を誤変換しないため）
            if (value is double or float or decimal)
            {
                double serial = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (serial is > 20000 and < 80000)
                {
                    DateTime converted = DateTime.FromOADate(serial);
                    return (converted.Year, converted.Month, converted.Day);
                }
                return null;
            }

            string? cleaned = TextUtil.Clean(value);
            if (cleaned == null)
            {
                return null;
            }

            //"令和8年5月30日" → "R8年5月30日" に寄せてから判定する
            string s = cleaned.Normalize(NormalizationForm.FormKC)
                              .Replace(" ", string.Empty)
                              .Replace("令和", "R")
                              .Replace("平成", "H");

            Match m = ReiwaRegex().Match(s);
            if (m.Success)
            {
                return Build(REIWA_OFFSET + int.Parse(m.Groups[1].Value), m);
            }

            m = HeiseiRegex().Match(s);
            if (m.Success)
            {
                return Build(HEISEI_OFFSET + int.Parse(m.Groups[1].Value), m);
            }

            m = SeirekiRegex().Match(s);
            if (m.Success)
            {
                return Build(int.Parse(m.Groups[1].Value), m);
            }

            return null;
        }

        private static (int y, int m, int d)? Build(int year, Match m)
        {
            int month = int.Parse(m.Groups[2].Value);
            int day = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
            if (month is < 1 or > 12 || day is < 0 or > 31)
            {
                return null;
            }
            return (year, month, day);
        }
    }
}
