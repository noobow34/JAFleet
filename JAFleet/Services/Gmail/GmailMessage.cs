namespace JAFleet.Services.Gmail
{
    /// <summary>Gmailの1通。必要な範囲（ヘッダ・本文テキスト・添付の見出し）だけを持つ。</summary>
    public class GmailMessage
    {
        public string Id { get; set; } = string.Empty;

        public string? Subject { get; set; }

        /// <summary>Fromヘッダそのまま（"名前 &lt;addr&gt;" の形）</summary>
        public string? From { get; set; }

        /// <summary>Toヘッダ。航空局のメールは問い合わせ先（register+内線番号@）宛てに送られてくる。</summary>
        public string? To { get; set; }

        /// <summary>送信日時（日本時間）。パスワードの「送信年月日」はこの日付。</summary>
        public DateTimeOffset SentAt { get; set; }

        public DateOnly SendDate => DateOnly.FromDateTime(SentAt.DateTime);

        /// <summary>text/plainの本文。内線番号を読み取るのに使う。</summary>
        public string? BodyText { get; set; }

        public List<GmailAttachment> Attachments { get; set; } = [];
    }

    public class GmailAttachment
    {
        /// <summary>
        /// MIMEの木の中での位置（"1" など）。
        /// attachmentIdと違いメールを取り直しても変わらないので、どの添付かを指すのはこちらを使う。
        /// </summary>
        public string? PartId { get; set; }

        /// <summary>
        /// 添付の取得に使うID。小さい添付は本体が直接返ってくることがあり、その場合はnull。
        /// **メールを取得するたびに違う値になる**ので、画面と往復させてはいけない
        /// （取得した応答の中でだけ有効な使い捨ての値として扱う）。
        /// </summary>
        public string? AttachmentId { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string? MimeType { get; set; }

        public int Size { get; set; }

        /// <summary>本体が直接返ってきた場合の中身</summary>
        public byte[]? InlineData { get; set; }

        public bool IsXlsx => FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                           || FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase);
    }
}
