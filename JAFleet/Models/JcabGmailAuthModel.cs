namespace JAFleet.Models
{
    /// <summary>
    /// Gmailのrefresh tokenを取るための画面（開発環境専用）のモデル。
    /// 取れたトークンは画面に出すだけで、どこにも保存しない。
    /// </summary>
    public class JcabGmailAuthModel : BaseModel
    {
        /// <summary>Google Cloud Consoleに登録するリダイレクトURI。実行中のURLから組み立てる。</summary>
        public string RedirectUri { get; set; } = string.Empty;

        /// <summary>Googleの同意画面のURL。クライアントが未設定のときはnull。</summary>
        public string? ConsentUrl { get; set; }

        /// <summary>交換で得たrefresh token。passに入れる値。</summary>
        public string? RefreshToken { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
