using System.ComponentModel.DataAnnotations;
using JAFleet.Commons.Data;

namespace JAFleet.Models
{
    public class EditModel : BaseModel
    {
        public Aircraft? Aircraft { get; set; }
        public bool IsNew { get; set; } = false;

        //選択用リスト
        [Display(Name = "航空会社")]
        public Airline[]? AirlineList { get; set; }

        [Display(Name = "型式")]
        public JAFleet.Commons.Data.Type[]? TypeList { get; set; }

        [Display(Name = "詳細型式")]
        public JAFleet.Commons.Data.TypeDetail[]? TypeDetailList { get; set; }

        [Display(Name = "運用状況")]
        public Code[]? OperationList { get; set; }

        [Display(Name = "WiFi")]
        public Code[]? WiFiList { get; set; }

        [Display(Name = "シートコンフィグ")]
        public SeatConfiguration[]? SeatConfigurationList { get; set; }

        [Display(Name = "履歴を作成しない")]
        public bool NotUpdateDate { get; set; } = false;

        /// <summary>
        /// 予約日。入っていればすぐには更新せず、この日に日次ジョブが反映するよう予約する（既存の機体だけ）
        /// </summary>
        [Display(Name = "予約日")]
        [DataType(DataType.Date)]
        public DateTime? ReserveDate { get; set; }

        /// <summary>この機体の予約中の更新。画面の上に出す</summary>
        public List<AircraftReservationRow> Reservations { get; set; } = [];

        /// <summary>予約・取消の結果。リダイレクトをまたぐのでTempDataで受け取る</summary>
        public string? Message { get; set; }

        public string? ErrorMessage { get; set; }

        public Exception? ex { get; set; }

        public string? LinkPage { get; set; }
    }
}
