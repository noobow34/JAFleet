namespace JAFleet.Models
{
    public class NotWorkingInfoModel : BaseModel
    {
        /// <summary>稼働状況を最後に更新したジョブと時刻（まだ一度も更新されていなければ null）</summary>
        public JAFleet.Services.WorkingStatusUpdateInfo? LastUpdate { get; set; }
    }
}
