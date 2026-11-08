using JAFleet.Commons.Data;
using Type = JAFleet.Commons.Data.Type;

namespace JAFleet.Models
{
    /// <summary>コンフィグ（座席仕様）マスタの画面</summary>
    public class SeatConfigModel : BaseModel
    {
        public List<SeatConfigRow> Rows { get; set; } = [];

        /// <summary>航空会社の選択肢。削除済みの会社は出さない。</summary>
        public Airline[] AirlineList { get; set; } = [];

        public Type[] TypeList { get; set; } = [];

        /// <summary>絞り込みに使っている航空会社コード</summary>
        public string? FilterAirline { get; set; }

        /// <summary>絞り込みに使っている型式コード</summary>
        public string? FilterType { get; set; }

        /// <summary>絞り込み前の全件数</summary>
        public int TotalCount { get; set; }

        /// <summary>新規登録の入力内容。エラーで戻ってきたときに入力し直せるよう保持する。</summary>
        public SeatConfigInput New { get; set; } = new();

        public string? Message { get; set; }

        public string? ErrorMessage { get; set; }

        /// <summary>エラーになった行。その行だけ入力内容を画面に残す。</summary>
        public int? ErrorConfigId { get; set; }
    }

    /// <summary>コンフィグ1件分。seat_configurationの内容と、そのコンフィグを使っている機体数を合わせて持つ。</summary>
    public class SeatConfigRow
    {
        public required int ConfigId { get; set; }

        public string? Airline { get; set; }

        /// <summary>航空会社の略称。マスタに無ければコードをそのまま出す。</summary>
        public string? AirlineName { get; set; }

        public string? Type { get; set; }

        /// <summary>型式名。マスタに無ければコードをそのまま出す。</summary>
        public string? TypeName { get; set; }

        public string? Url { get; set; }

        public int? F { get; set; }

        public int? C { get; set; }

        public int? PY { get; set; }

        public int? P { get; set; }

        public int? J { get; set; }

        public int? Y { get; set; }

        /// <summary>「C12,Y134 (146)」のような表示用の文字列</summary>
        public string SeatConfig { get; set; } = string.Empty;

        /// <summary>このコンフィグを指している機体の数。1件でもあると削除できない。</summary>
        public int UsedCount { get; set; }
    }

    /// <summary>登録・更新の入力値。画面のフォームからそのまま受け取る。</summary>
    public class SeatConfigInput
    {
        /// <summary>更新する行のID。新規登録ではnull。</summary>
        public int? ConfigId { get; set; }

        public string? Airline { get; set; }

        public string? Type { get; set; }

        public string? Url { get; set; }

        public int? F { get; set; }

        public int? C { get; set; }

        public int? PY { get; set; }

        public int? P { get; set; }

        public int? J { get; set; }

        public int? Y { get; set; }
    }
}
