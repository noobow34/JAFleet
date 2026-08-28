using JAFleet.Commons.EF;

namespace JAFleet.Models
{
    public class HomeModel : BaseModel
    {
        public List<Airline>? ana { get; set; }
        public List<Airline>? jal { get; set; }
        public List<Airline>? lcc { get; set; }
        public List<Airline>? other { get; set; }
    }
}
