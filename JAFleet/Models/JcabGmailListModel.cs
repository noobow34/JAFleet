namespace JAFleet.Models
{
    /// <summary>
    /// 取込画面の「Gmailから取り込む」に出すメールの一覧（部分ビュー _GmailList 用）。
    /// Gmailへの問い合わせは遅くなりうるので、画面の枠とは分けて後から差し込む。
    /// </summary>
    public class JcabGmailListModel
    {
        /// <summary>この一覧を出すのに使った検索式</summary>
        public string Query { get; set; } = string.Empty;

        /// <summary>管理コンソールから来たか（取込後の戻り先に引き継ぐ）</summary>
        public bool FromAdmin { get; set; }

        public string? ErrorMessage { get; set; }

        public List<JcabGmailMessageModel> Messages { get; set; } = [];
    }

    public class JcabGmailMessageModel
    {
        public string MessageId { get; set; } = string.Empty;

        public string? Subject { get; set; }

        /// <summary>差出人。担当官個人のアドレスなので異動すると変わる。</summary>
        public string? From { get; set; }

        /// <summary>送信日時（日本時間）。パスワードの「送信年月日」はこの日付。</summary>
        public DateTimeOffset SentAt { get; set; }

        /// <summary>件名から読んだ対象月（yyyy/MM）。Excelを開く前の目安。</summary>
        public string? TargetMonth { get; set; }

        /// <summary>再送メールか。再送分のパスワードは元のメールの送信年月日のまま。</summary>
        public bool IsResend { get; set; }

        public List<JcabGmailAttachmentModel> Attachments { get; set; } = [];

        /// <summary>このメールから作った一時保存。あれば取込ではなく再開へ誘導する。</summary>
        public int? SessionId { get; set; }
    }

    public class JcabGmailAttachmentModel
    {
        /// <summary>
        /// どの添付かを指す値。Gmailのattachment_idはメールを取得するたびに変わるため、
        /// 画面と往復させるのは取り直しても変わらないpartIdにする。
        /// </summary>
        public string? PartId { get; set; }

        public string FileName { get; set; } = string.Empty;

        public int Size { get; set; }
    }
}
