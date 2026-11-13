namespace JAFleet.Services.Livery
{
    /// <summary>特別塗装ニュースのチェックで使う固定値</summary>
    public static class LiveryNewsConstant
    {
        /// <summary>Aviation WireのRSS。1時間ごとに更新され、直近10本ほどが載っている</summary>
        public const string RSS_URL = "https://www.aviationwire.jp/feed";

        /// <summary>
        /// RSSを取るときに名乗る値。
        /// Aviation Wireは<b>User-Agentが空のリクエストを403で返す</b>（ヘッダの中身は見ておらず、
        /// 何か入っていれば通る）。.NETのHttpClientは既定でUser-Agentを送らないため、
        /// 入れずに叩くと必ず403になる。
        /// </summary>
        public const string USER_AGENT = "Mozilla/5.0 (compatible; RSS fetcher)";

        public const string GEMINI_API_URL_TEMPLATE =
            "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

        /// <summary>判定に使うモデル。本文はurl_contextで読ませるので軽いもので足りる</summary>
        public const string GEMINI_MODEL = "gemini-3.5-flash-lite";

        /// <summary>
        /// 1回の実行でGeminiへ投げる記事の上限。
        /// RSSには直近20本ほどしか載らないので通常は超えないが、
        /// 長く止まっていた直後などに一気に投げてしまわないための歯止め。
        /// </summary>
        public const int MAX_ITEMS_PER_RUN = 30;

        /// <summary>記事ごとの待機（ms）。無料枠のレート制限に当てないため</summary>
        public const int DELAY_BETWEEN_ITEMS = 3000;

        /// <summary>レート制限に当たったときの待機（ms）</summary>
        public const int RATE_LIMIT_RETRY_DELAY = 65000;

        public const int MAX_RETRY = 3;

        /// <summary>塗装名の長さの上限。打ち間違いと、記事の文章がそのまま入るのを弾くためだけの値</summary>
        public const int MAX_LIVERY_LENGTH = 100;

        public const string SITE_URL = "https://ja-fleet.noobow.me";
    }
}
