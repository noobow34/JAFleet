namespace JAFleet.Models
{
    /// <summary>サイト設定の画面</summary>
    public class SiteSettingModel : BaseModel
    {
        /// <summary>公開メニューに「非稼働情報」を出すか</summary>
        public bool ShowNotWorkingInfoMenu { get; set; }

        /// <summary>稼働状況の取得元</summary>
        public JAFleet.Services.WorkingStatusSource WorkingStatusSource { get; set; }

        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
