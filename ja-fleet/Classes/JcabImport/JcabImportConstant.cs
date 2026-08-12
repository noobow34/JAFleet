namespace jafleet.Classes.JcabImport
{
    public static class JcabImportConstant
    {
        /// <summary>codeテーブルに取込設定を持たせるためのcode_type</summary>
        public const string CODE_TYPE = "IMPORT";

        /// <summary>内線番号のキー</summary>
        public const string KEY_EXTENSION = "EXTENSION";

        public const string XLSX_MIME = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        /// <summary>送信年月日を何日前まで遡ってパスワードを試すか</summary>
        public const int PASSWORD_RETRY_DAYS = 7;

        /// <summary>復号済みファイルをキャッシュに置いておく時間</summary>
        public static readonly TimeSpan CACHE_LIFETIME = TimeSpan.FromMinutes(30);
    }
}
