namespace JAFleet.Services.JcabImport
{
    public static class JcabImportConstant
    {
        /// <summary>codeテーブルに取込設定を持たせるためのcode_type。この列は3桁までしか入らない。</summary>
        public const string CODE_TYPE = "IMP";

        /// <summary>内線番号のキー。keyもOPEなど既存コードと同じく1文字で運用する。</summary>
        public const string KEY_EXTENSION = "1";

        public const string XLSX_MIME = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        /// <summary>
        /// Gmailから取込候補を探す検索式。
        /// 差出人は担当官個人のアドレスで異動のたびに変わるためドメインで見る。
        /// 件名の「航空機登録情報」は再送（(再送)や【再送】が前に付く）でも共通。
        /// in:inboxを付けているのは、取り込んだらアーカイブする運用で
        /// 「受信トレイに残っている＝まだ取り込んでいない」ことにするため。
        /// 済んだ分を掘り返したいときは画面で一時的に外せる。
        /// </summary>
        public const string GMAIL_QUERY = "from:mlit.go.jp subject:航空機登録情報 has:attachment in:inbox";

        /// <summary>Gmailから一度に取ってくる件数。1通ずつAPIを叩くので欲張らない。</summary>
        public const int GMAIL_SEARCH_MAX = 10;

        /// <summary>送信年月日を何日前まで遡ってパスワードを試すか</summary>
        public const int PASSWORD_RETRY_DAYS = 7;

        /// <summary>復号済みファイルをキャッシュに置いておく時間</summary>
        public static readonly TimeSpan CACHE_LIFETIME = TimeSpan.FromMinutes(30);
    }
}
