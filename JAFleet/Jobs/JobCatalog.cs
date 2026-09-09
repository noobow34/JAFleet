using System.ComponentModel;
using System.Reflection;
using Quartz;

namespace JAFleet.Jobs
{
    /// <summary>
    /// このアセンブリにあるQuartzジョブの一覧。
    /// 管理画面がクラス名と説明を引くのに使う。クラス名はscheduler_defに入れる値と同じ形。
    /// </summary>
    public static class JobCatalog
    {
        private static readonly Lazy<Dictionary<string, JobInfo>> jobs = new(() =>
            typeof(JobCatalog).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IJob).IsAssignableFrom(t) && t.FullName != null)
                .Select(t => new JobInfo(
                    t.FullName!,
                    t.Name,
                    t.GetCustomAttribute<DescriptionAttribute>()?.Description,
                    t))
                .OrderBy(j => j.DisplayName, StringComparer.Ordinal)
                .ToDictionary(j => j.ClassName, StringComparer.Ordinal));

        public static IReadOnlyCollection<JobInfo> All => jobs.Value.Values;

        public static JobInfo? Find(string className)
            => jobs.Value.TryGetValue(className, out JobInfo? info) ? info : null;
    }

    /// <summary>ジョブ1つ分の見出し情報</summary>
    public record JobInfo(string ClassName, string DisplayName, string? Description, Type JobType);
}
