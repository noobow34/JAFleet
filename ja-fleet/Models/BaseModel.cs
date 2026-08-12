namespace jafleet.Models
{
    public class BaseModel
    {
        public bool NoHead { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsDetail { get; set; }
        public string? Title { get; set; }

        /// <summary>管理コンソールから遷移してきた場合にtrue。戻るボタンの表示に使う。</summary>
        public bool FromAdmin { get; set; }
        public string? TableId { get; set; }
        public string? api { get; set; }
    }
}
