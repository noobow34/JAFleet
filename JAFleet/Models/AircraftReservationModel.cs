namespace JAFleet.Models
{
    /// <summary>機体更新の予約の一覧（/AircraftReservation）</summary>
    public class AircraftReservationModel : BaseModel
    {
        /// <summary>予約中のもの。反映日の近い順</summary>
        public List<AircraftReservationRow> Pending { get; set; } = [];

        /// <summary>反映・取消・失敗したもの（直近分）</summary>
        public List<AircraftReservationRow> Resolved { get; set; } = [];

        /// <summary>反映ジョブがスケジューラに載っているか。載っていなければ予約日が来ても反映されない</summary>
        public bool JobScheduled { get; set; }

        /// <summary>反映ジョブの次回実行日時</summary>
        public DateTime? NextFireTime { get; set; }

        /// <summary>絞り込んでいるレジ。nullなら全部</summary>
        public string? Reg { get; set; }

        public string? Message { get; set; }

        public string? ErrorMessage { get; set; }
    }

    /// <summary>予約1件分</summary>
    public class AircraftReservationRow
    {
        public int ReservationId { get; set; }

        public string RegistrationNumber { get; set; } = string.Empty;

        public DateTime ApplyDate { get; set; }

        public List<AircraftReservationChangeRow> Changes { get; set; } = [];

        public bool WriteHistory { get; set; }

        public string? Source { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime? ResolvedAt { get; set; }

        public string? ResultMessage { get; set; }
    }

    /// <summary>予約した項目1つ分を画面に出す形にしたもの</summary>
    public record AircraftReservationChangeRow(string FieldName, string Before, string After);
}
