namespace jafleet.Classes.JcabImport
{
    public static class JcabImportConstant
    {
        /// <summary>codeテーブルに取込設定を持たせるためのcode_type。この列は3桁までしか入らない。</summary>
        public const string CODE_TYPE = "IMP";

        /// <summary>内線番号のキー。keyもOPEなど既存コードと同じく1文字で運用する。</summary>
        public const string KEY_EXTENSION = "1";

        public const string XLSX_MIME = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        /// <summary>送信年月日を何日前まで遡ってパスワードを試すか</summary>
        public const int PASSWORD_RETRY_DAYS = 7;

        /// <summary>復号済みファイルをキャッシュに置いておく時間</summary>
        public static readonly TimeSpan CACHE_LIFETIME = TimeSpan.FromMinutes(30);
    }
}
