using AutoMapper;
using JAFleet.Commons.Data;
using JAFleet.Models;
using Newtonsoft.Json;

namespace JAFleet.Services
{
    /// <summary>
    /// 棚卸（/Inventory）の進み具合と完了の判定。DB には触らない（呼ぶ側が保存する）
    /// </summary>
    public static class InventoryStore
    {
        private static readonly IMapper ConditionMapper = new MapperConfiguration(
            cfg => cfg.CreateMap<SearchConditionInModel, SearchModel>(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).CreateMapper();

        /// <summary>保存した条件の JSON（検索画面の保存済み条件と同じ形）を、絞り込みに渡せる形にする</summary>
        public static SearchModel ToSearchModel(string conditionJson)
        {
            var scm = JsonConvert.DeserializeObject<SearchConditionInModel>(conditionJson,
                new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate })!;
            return ConditionMapper.Map<SearchModel>(scm);
        }

        /// <summary>目印のあとで機体が更新されたか（要再確認）</summary>
        public static bool IsStale(DateTime? checkedAt, DateTime? updated) =>
            checkedAt != null && updated != null && updated > checkedAt;

        /// <summary>確認済（目印があり、要再確認でない）か</summary>
        public static bool IsChecked(IReadOnlyDictionary<string, DateTime> checks, AircraftView aircraft) =>
            checks.TryGetValue(aircraft.RegistrationNumber, out DateTime checkedAt) && !IsStale(checkedAt, aircraft.ActualUpdateTime);

        /// <summary>
        /// 条件に当たる機体と目印から進み具合を数え直す。未完了で、1 機以上あって全部確認済なら完了にする（自動）。
        /// 完了したあとで未確認が出ても（機体が増えた・更新された）完了のままにする。戻すのは画面から手で行う
        /// </summary>
        public static void UpdateProgress(InventorySession session, IReadOnlyCollection<AircraftView> aircraft,
            IReadOnlyDictionary<string, DateTime> checks, DateTime now)
        {
            session.TotalCount = aircraft.Count;
            session.CheckedCount = aircraft.Count(a => IsChecked(checks, a));
            if (session.CompletedAt == null && session.TotalCount > 0 && session.CheckedCount == session.TotalCount)
            {
                session.CompletedAt = now;
                session.CompletedManually = false;
            }
        }
    }
}
