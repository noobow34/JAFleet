using JAFleet.Commons.Data;

namespace JAFleet.Models
{
    public class SearchResult : BaseModel
    {
        public string? SearchConditionKey { get; set; }
        public AircraftView[]? ResultList { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
