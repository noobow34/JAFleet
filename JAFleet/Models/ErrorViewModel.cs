namespace JAFleet.Models
{
    public class ErrorViewModel : BaseModel
    {
        public required string RequestId { get; set; }

        public Exception? Ex { get; set; }

        public bool? ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}