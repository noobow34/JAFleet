using JAFleet.Commons.Data;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Services
{
    /// <summary>
    /// site_setting テーブルの内容をメモリに持つ。全ページのレイアウトから読むので、毎回DBには行かない。
    /// 起動時とマスタ再読込で読み込み、/SiteSetting で保存したときにも差し替える。
    /// </summary>
    public static class SiteSettings
    {
        /// <summary>公開メニューに「非稼働情報」を出すか。既定は出す。</summary>
        public const string SHOW_NOT_WORKING_INFO_MENU = "show_not_working_info_menu";

        /// <summary>稼働状況をどこから作るか（"adsb" / "fr24"）。既定は adsb。</summary>
        public const string WORKING_STATUS_SOURCE = "working_status_source";

        private static volatile IReadOnlyDictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

        public static bool ShowNotWorkingInfoMenu => ReadBool(_values, SHOW_NOT_WORKING_INFO_MENU, true);

        /// <summary>
        /// 稼働状況の取得元。adsb なら adsb.lol の履歴ダンプ（RefreshAdsbFlightJob）が、fr24 なら FR24 の巡回
        /// （RefreshWorkingStatusAndPhotoJob と NotifyWorkingStatusJob）が working_status と稼働通知を受け持つ。
        /// FR24 が自動アクセスを許さなくなったので既定は adsb。FR24 が戻ったときに切り替えられるよう残している。
        /// </summary>
        public static WorkingStatusSource WorkingStatusSource => ReadSource(_values);

        public static WorkingStatusSource ReadSource(IReadOnlyDictionary<string, string> values)
            => values.TryGetValue(WORKING_STATUS_SOURCE, out string? text)
               && string.Equals(text?.Trim(), "fr24", StringComparison.OrdinalIgnoreCase)
                ? WorkingStatusSource.Fr24
                : WorkingStatusSource.Adsb;

        public static void SaveSource(JAFleetContext context, WorkingStatusSource source)
            => SaveText(context, WORKING_STATUS_SOURCE, source == WorkingStatusSource.Fr24 ? "fr24" : "adsb");

        /// <summary>DBから読み直す。読めなかったときは直前の値（起動直後なら既定値）のまま動く。</summary>
        public static void Load(JAFleetContext context)
        {
            try
            {
                _values = context.SiteSettings.AsNoTracking()
                    .ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                //テーブルが無い（DDL未適用）などで読めなくても既定値で動かす。
                //全ページのレイアウトが使うので、ここで落とすとサイト全体が止まる。
                Console.WriteLine($"[SiteSettings] site_settingを読めないため既定値で動きます: {ex.Message}");
            }
        }

        /// <summary>真偽値の設定を保存してメモリにも反映する。テーブルが無いときは例外になる。</summary>
        public static void SaveBool(JAFleetContext context, string key, bool value)
            => SaveText(context, key, value ? "true" : "false");

        /// <summary>文字列の設定を保存してメモリにも反映する。テーブルが無いときは例外になる。</summary>
        public static void SaveText(JAFleetContext context, string key, string text)
        {
            SiteSetting? row = context.SiteSettings.FirstOrDefault(s => s.Key == key);
            if (row == null)
            {
                context.SiteSettings.Add(new SiteSetting { Key = key, Value = text, UpdatedAt = DateTime.Now });
            }
            else
            {
                row.Value = text;
                row.UpdatedAt = DateTime.Now;
            }
            context.SaveChanges();

            //保存できたものはそのまま反映する（読み直しが失敗して古い値が残るのを避ける）
            _values = new Dictionary<string, string>(_values, StringComparer.Ordinal) { [key] = text };
        }

        /// <summary>行が無い・値が真偽値として読めないときは既定値を返す。</summary>
        public static bool ReadBool(IReadOnlyDictionary<string, string> values, string key, bool defaultValue)
        {
            if (!values.TryGetValue(key, out string? text))
            {
                return defaultValue;
            }
            return bool.TryParse(text?.Trim(), out bool value) ? value : defaultValue;
        }
    }

    /// <summary>稼働状況の取得元</summary>
    public enum WorkingStatusSource
    {
        /// <summary>adsb.lol の履歴ダンプ（GitHub）</summary>
        Adsb,
        /// <summary>Flightradar24 の機体ページの巡回</summary>
        Fr24,
    }
}
