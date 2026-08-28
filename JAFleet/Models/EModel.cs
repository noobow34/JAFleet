using System.ComponentModel.DataAnnotations;
using JAFleet.Commons.EF;

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
        public JAFleet.Commons.EF.Type[]? TypeList { get; set; }

        [Display(Name = "詳細型式")]
        public JAFleet.Commons.EF.TypeDetail[]? TypeDetailList { get; set; }

        [Display(Name = "運用状況")]
        public Code[]? OperationList { get; set; }

        [Display(Name = "WiFi")]
        public Code[]? WiFiList { get; set; }

        [Display(Name = "シートコンフィグ")]
        public SeatConfiguration[]? SeatConfigurationList { get; set; }

        [Display(Name = "履歴を作成しない")]
        public bool NotUpdateDate { get; set; } = false;

        public Exception? ex { get; set; }

        public string? LinkPage { get; set; }
    }
}
