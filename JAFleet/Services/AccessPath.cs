namespace JAFleet.Services
{
    /// <summary>
    /// access_logの行を集計向けに読み解く。
    /// コントローラーの増減に合わせてここも足す。
    /// </summary>
    public static class AccessPath
    {
        /// <summary>機体詳細のパスの先頭セグメント。派生（ヘッダ無し・埋め込み）も同じ1機として数える</summary>
        private static readonly string[] DETAIL_SEGMENTS = ["AD", "ADN", "ADNB", "ADE", "AIRCRAFTDETAIL"];

        /// <summary>
        /// 人が見るページではないコントローラー。
        /// 静的ファイルはそもそもルートに当たらない（route_keyがnull）ので、ここには並べない。
        /// </summary>
        public static readonly string[] NON_PAGE_CONTROLLERS =
        [
            //  /api 配下のJSON API。外部から直接叩かれる
            "AirlineGroup", "Airline", "Type", "Reg", "AircraftWithHistory",
            //  画面の中から叩くAJAX
            "Master",
            //  押すと処理が走るだけで画面を返さないもの
            "Batch", "ReloadMaster", "SetCookie",
            //  死活監視・動作確認用
            "Check", "CheckError", "Dummy", "Sandbox",
        ];

        /// <summary>コントローラー名を日本語の呼び名にする。無いものは名前をそのまま出す</summary>
        private static readonly Dictionary<string, string> CONTROLLER_NAMES = new()
        {
            ["Home"]            = "トップ",
            ["AircraftDetail"]  = "機体詳細",
            ["Aircraft"]        = "機体一覧",
            ["Search"]          = "検索",
            ["NotWorkingInfo"]  = "非稼働情報",
            ["WorkingCheckLog"] = "稼働チェックログ",
            ["Line"]            = "LINE",
            ["Message"]         = "問い合わせ",
            ["Admin"]           = "管理コンソール",
            ["E"]               = "単票編集",
            ["Log"]             = "アクセスログ",
            ["AccessStats"]     = "アクセス集計",
            ["JcabImport"]      = "航空局Excel取込",
            ["BulkRegister"]    = "一括登録",
            ["Scheduler"]       = "定期実行ジョブ",
            ["Account"]         = "ログイン",
        };

        /// <summary>route_keyが人の見るページを指しているか</summary>
        public static bool IsPage(string? routeKey)
        {
            string controller = Controller(routeKey);
            return controller.Length != 0 && !NON_PAGE_CONTROLLERS.Contains(controller);
        }

        /// <summary>"コントローラー名/アクション名" からコントローラー名だけを取る</summary>
        public static string Controller(string? routeKey)
        {
            if (string.IsNullOrEmpty(routeKey))
            {
                return string.Empty;
            }
            int slash = routeKey.IndexOf('/');
            return slash < 0 ? routeKey : routeKey[..slash];
        }

        /// <summary>画面に出すコントローラーの呼び名</summary>
        public static string DisplayName(string? routeKey)
        {
            string controller = Controller(routeKey);
            if (controller.Length == 0)
            {
                return "その他";
            }
            return CONTROLLER_NAMES.TryGetValue(controller, out string? name) ? name : controller;
        }

        // ===== ここから移行措置 =====
        // route_key列を足す前に記録された行はroute_keyがnullで、そのままだと集計から丸ごと落ちる。
        // 過去31日分が空に見えてしまうので、その間だけパスから推測して拾う。
        // access_logは31日で消えるため、デプロイから1か月たてばこの区画はまるごと削除してよい。

        /// <summary>
        /// route_keyが無い行でページではないと見なすパスの頭。小文字で比べる。
        /// NON_PAGE_CONTROLLERSと対応するが、JSON APIは"/api"配下にまとまっている
        /// </summary>
        public static readonly string[] LEGACY_NON_PAGE_PREFIXES =
        [
            "/api", "/master", "/batch", "/reloadmaster", "/setcookie",
            "/check", "/dummy", "/sandbox",
        ];

        /// <summary>
        /// route_keyが無い行のコントローラーの呼び名を、パスの先頭から推測する。
        /// ルーティングの別名（/AD→AircraftDetail など）はここで吸収する
        /// </summary>
        private static readonly Dictionary<string, string> LEGACY_SEGMENT_CONTROLLERS = new()
        {
            ["AD"]             = "AircraftDetail",
            ["ADN"]            = "AircraftDetail",
            ["ADNB"]           = "AircraftDetail",
            ["ADE"]            = "AircraftDetail",
            ["AIRCRAFTDETAIL"] = "AircraftDetail",
            ["LOG"]            = "Log",
            ["LOGY"]           = "Log",
        };

        /// <summary>route_keyが無い行のコントローラーの呼び名</summary>
        public static string LegacyDisplayName(string? path)
        {
            string[] segments = Segments(path);
            if (segments.Length == 0)
            {
                return CONTROLLER_NAMES["Home"];
            }

            string head = segments[0].ToUpperInvariant();
            if (LEGACY_SEGMENT_CONTROLLERS.TryGetValue(head, out string? mapped))
            {
                return DisplayName(mapped);
            }

            //コントローラー名は大文字小文字が揃っていないので、突き合わせは大文字で行う
            string? known = CONTROLLER_NAMES.Keys.FirstOrDefault(c => c.ToUpperInvariant() == head);
            return known != null ? CONTROLLER_NAMES[known] : segments[0];
        }

        // ===== ここまで移行措置 =====

        /// <summary>機体詳細のパスならレジを返す。それ以外はnull</summary>
        public static string? DetailRegistration(string? path)
        {
            string[] segments = Segments(path);
            if (segments.Length == 0 || !DETAIL_SEGMENTS.Contains(segments[0].ToUpperInvariant()))
            {
                return null;
            }
            return segments.Length >= 2 && segments[1].Length != 0 ? segments[1].ToUpperInvariant() : null;
        }

        private static string[] Segments(string? path)
            => (path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
    }
}
