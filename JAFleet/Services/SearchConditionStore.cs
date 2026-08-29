using EnumStringValues;
using JAFleet.Commons.Data;
using JAFleet.Models;
using JAFleet.Infrastructure;
using Noobow.Commons.Constants;
using Noobow.Commons.Utils;

namespace JAFleet.Services
{
    /// <summary>
    /// 名前付き検索条件の登録。検索画面（/Search）とExcel取込の両方から使う。
    /// キーは条件JSONのCRC32なので、条件が1つでも変われば別のキーになる。
    /// </summary>
    public static class SearchConditionStore
    {
        /// <summary>
        /// 検索条件に名前を付けて登録する。
        /// 同じ名前が別の条件に付いていた場合は、そちらの名前を外して付け替える。
        /// 行自体は消さない。アクセスログが検索条件キーを参照しているため。
        /// </summary>
        /// <returns>登録した検索条件のキー</returns>
        public static string Save(JAFleetContext context, SearchConditionInModel condition, string name, bool notifySlack = true)
        {
            string json = condition.ToString();
            string hash = HashUtil.CalcCRC32(json);

            List<SearchCondition> sameName = context.SearchConditions
                .Where(sc => sc.SearchConditionName == name && sc.SearchConditionKey != hash)
                .ToList();
            foreach (SearchCondition old in sameName)
            {
                old.SearchConditionName = null;
            }

            SearchCondition? target = context.SearchConditions
                .SingleOrDefault(sc => sc.SearchConditionKey == hash);

            if (target != null)
            {
                target.SearchConditionName = name;
            }
            else
            {
                target = new SearchCondition
                {
                    SearchConditionKey = hash,
                    SearchConditionJson = json,
                    SearchConditionName = name,
                    SearchCount = 0,
                };
                context.SearchConditions.Add(target);
            }

            context.SaveChanges();
            MasterManager.ReloadNamedSearchCondition(context);

            if (notifySlack)
            {
                _ = Task.Run(async () =>
                {
                    await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(),
                        $"検索条件が登録されました。\n{name}\n{json}");
                });
            }

            return target.SearchConditionKey;
        }
    }
}
