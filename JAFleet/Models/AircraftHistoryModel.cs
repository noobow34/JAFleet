using JAFleet.Commons.Data;
using Type = JAFleet.Commons.Data.Type;

namespace JAFleet.Models
{
    /// <summary>
    /// 更新履歴（aircraft_history）の画面。
    /// 一覧がそのまま入力欄になっていて、まとめて保存する。
    /// </summary>
    public class AircraftHistoryModel : BaseModel
    {
        /// <summary>表示中のレジ。未指定ならレジ検索だけを出す。</summary>
        public string? Reg { get; set; }

        /// <summary>レジがaircraftに無かった（履歴だけ残っている場合もあるので一覧は出す）</summary>
        public bool AircraftNotFound { get; set; }

        /// <summary>いまのaircraftの内容。比較用に一覧の先頭へ出すだけで、ここからは編集しない。</summary>
        public AircraftCurrentRow? Current { get; set; }

        /// <summary>履歴の編集内容。SEQの降順。</summary>
        public List<AircraftHistoryInput> Rows { get; set; } = [];

        public Airline[] AirlineList { get; set; } = [];

        public Type[] TypeList { get; set; } = [];

        public TypeDetail[] TypeDetailList { get; set; } = [];

        public Code[] OperationList { get; set; } = [];

        public Code[] WifiList { get; set; } = [];

        public SeatConfiguration[] SeatConfigurationList { get; set; } = [];

        public string? Message { get; set; }

        public string? ErrorMessage { get; set; }

        /// <summary>エラーになった行。その行に目印を出す。</summary>
        public int? ErrorAhId { get; set; }
    }

    /// <summary>いまの機体情報を一覧の先頭に出すための行。表示専用。</summary>
    public class AircraftCurrentRow
    {
        public DateTime? UpdateTime { get; set; }

        public string? AirlineName { get; set; }

        public string? TypeDetailName { get; set; }

        public string? SerialNumber { get; set; }

        public string? RegisterDate { get; set; }

        public string? OperationName { get; set; }

        public string? WifiName { get; set; }

        public string? SeatConfigText { get; set; }

        public string? SpecialLivery { get; set; }

        public string? Remarks { get; set; }
    }

    /// <summary>履歴1行分の編集内容。aircraft_historyの1行に対応する。</summary>
    public class AircraftHistoryInput
    {
        public int AhId { get; set; }

        /// <summary>レジごとの連番。詳細画面の履歴はこの順に並ぶ。</summary>
        public int? Seq { get; set; }

        public DateTime? HistoryRegisterAt { get; set; }

        public string? Airline { get; set; }

        public int TypeDetailId { get; set; }

        public string? SerialNumber { get; set; }

        public string? RegisterDate { get; set; }

        public string? OperationCode { get; set; }

        public string? WifiCode { get; set; }

        public int? SeatConfig { get; set; }

        public string? SpecialLivery { get; set; }

        public string? Remarks { get; set; }

        public string? LinkUrl { get; set; }

        public string? TestRegistration { get; set; }

        public bool MaintenanceNotify { get; set; }

        public DateTime? CreationTime { get; set; }

        public DateTime? UpdateTime { get; set; }

        public DateTime? ActualUpdateTime { get; set; }
    }
}
