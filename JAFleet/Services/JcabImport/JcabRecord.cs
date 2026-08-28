namespace JAFleet.Services.JcabImport
{
    public enum JcabRecordType
    {
        /// <summary>新規登録</summary>
        New,
        /// <summary>抹消登録</summary>
        Delete,
        /// <summary>移転登録</summary>
        Transfer,
        /// <summary>変更登録</summary>
        Change,
        /// <summary>予約登録受付</summary>
        Reservation,
        /// <summary>予約登録取り下げ</summary>
        Cancel,
    }

    public static class JcabRecordTypeExtensions
    {
        public static string ToJapanese(this JcabRecordType type) => type switch
        {
            JcabRecordType.New => "新規登録",
            JcabRecordType.Delete => "抹消登録",
            JcabRecordType.Transfer => "移転登録",
            JcabRecordType.Change => "変更登録",
            JcabRecordType.Reservation => "予約登録",
            JcabRecordType.Cancel => "予約取下",
            _ => type.ToString(),
        };
    }

    /// <summary>航空局Excelの1レコード（登録記号1つ分の1イベント）</summary>
    public class JcabRecord
    {
        public JcabRecordType RecordType { get; set; }

        public string SheetName { get; set; } = string.Empty;

        /// <summary>Excel上の開始行。プレビューから原本を照合するために持つ。</summary>
        public int Row { get; set; }

        /// <summary>JA付きの登録記号</summary>
        public string RegistrationNumber { get; set; } = string.Empty;

        /// <summary>型式1行目（メーカー名）</summary>
        public string? Maker { get; set; }

        /// <summary>型式2行目</summary>
        public string? TypeName { get; set; }

        public string? SerialNumber { get; set; }

        /// <summary>定置場</summary>
        public string? BasePlace { get; set; }

        /// <summary>所有者・新所有者・申請者</summary>
        public string? Owner { get; set; }

        public string? OwnerAddress { get; set; }

        /// <summary>旧所有者（移転登録）</summary>
        public string? PreviousOwner { get; set; }

        public string? PreviousOwnerAddress { get; set; }

        /// <summary>共有者など、3行目以降に続く所有者情報</summary>
        public List<string> AdditionalOwners { get; set; } = [];

        /// <summary>yyyy/MM/dd</summary>
        public string? RegisterDate { get; set; }

        /// <summary>Excelに入っていた生の値（日付が読めなかったときの表示用）</summary>
        public string? RegisterDateRaw { get; set; }

        /// <summary>抹消原因</summary>
        public string? Reason { get; set; }

        /// <summary>変更事項（商号・住所・定置場）</summary>
        public string? ChangeItem { get; set; }

        public string? ChangeNew { get; set; }

        public string? ChangeOld { get; set; }

        /// <summary>納入予定年月日（予約登録）</summary>
        public string? ScheduledDate { get; set; }

        /// <summary>摘要</summary>
        public string? Note { get; set; }

        /// <summary>メーカー＋型式の表示用文字列</summary>
        public string TypeDisplay =>
            string.Join(" ", new[] { Maker, TypeName }.Where(s => !string.IsNullOrEmpty(s)));
    }
}
