using JAFleet.Commons.Data;

namespace JAFleet.Models
{
    public class HomeModel : BaseModel
    {
        public List<Airline>? ana { get; set; }
        public List<Airline>? jal { get; set; }
        public List<Airline>? lcc { get; set; }
        public List<Airline>? other { get; set; }

        /// <summary>航空会社コード → 機数（抹消済を除く。一覧を開いたときの初期表示と同じ数え方）</summary>
        public Dictionary<string, int> AirlineCounts { get; set; } = [];

        /// <summary>型式コード → 機数（抹消済を除く）。0 の型式は全機退役</summary>
        public Dictionary<string, int> TypeCounts { get; set; } = [];

        public int AirlineCount(string? code) => code != null && AirlineCounts.TryGetValue(code, out int n) ? n : 0;

        public int TypeCount(string? code) => code != null && TypeCounts.TryGetValue(code, out int n) ? n : 0;
    }
}
