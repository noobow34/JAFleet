namespace JAFleet.Models
{
    /// <summary>サイト設定の画面</summary>
    public class SiteSettingModel : BaseModel
    {
        /// <summary>公開メニューに「非稼働情報」を出すか</summary>
        public bool ShowNotWorkingInfoMenu { get; set; }

        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
