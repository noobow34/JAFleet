namespace jafleet.Models
{
    public class LogEntry
    {
        public DateTime? LogDate    { get; set; }
        public string?   LogTypeCode { get; set; }
        public string?   LogTypeName { get; set; }
        public string?   UserId     { get; set; }
        public string?   Detail     { get; set; }
    }

    public class LogModel : BaseModel
    {
        public DateTime      SearchDate { get; set; }
        public List<LogEntry> Entries    { get; set; } = new();
    }
}
