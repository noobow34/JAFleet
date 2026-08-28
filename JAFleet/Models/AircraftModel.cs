using System.ComponentModel.DataAnnotations;

namespace JAFleet.Models
{
    public class AircraftModel : BaseModel
    {
        [Display(Name = "2018/09以降の抹消済も表示")]
        public bool IncludeRetire { get; set; }
        public bool IsAllRetire { get; set; }
    }
}
