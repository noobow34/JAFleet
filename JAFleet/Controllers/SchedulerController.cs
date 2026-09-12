using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using JAFleet.Jobs;
using JAFleet.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace JAFleet.Controllers
{
    /// <summary>
    /// 定期実行ジョブ（scheduler_def）の画面。
    /// 有効・無効の切り替えとcron式の変更を、動いているQuartzへそのまま反映する。
    /// </summary>
    public class SchedulerController : Controller
    {
        private const string TITLE = "定期実行ジョブ";

        private readonly JAFleetContext _context;

        public SchedulerController(JAFleetContext context) => _context = context;

        public async Task<IActionResult> IndexAsync([FromQuery] bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            return View(await BuildModelAsync(fromAdmin));
        }

        /// <summary>1件分の定義を保存してスケジューラへ反映する。DBに行が無ければ追加する。</summary>
        [HttpPost]
        public async Task<IActionResult> SaveAsync(string className, string? cronDef, bool enabled, bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            className = (className ?? string.Empty).Trim();
            cronDef = (cronDef ?? string.Empty).Trim();

            SchedulerDef? def = _context.SchedulerDefs.FirstOrDefault(s => s.ClassName == className);
            JobInfo? info = JobCatalog.Find(className);

            string? invalid = Validate(className, cronDef, enabled, def, info);
            if (invalid != null)
            {
                SchedulerModel ng = await BuildModelAsync(fromAdmin);
                ng.ErrorMessage = invalid;
                //入力し直せるように、保存できなかった内容を画面に残す
                SchedulerRow? row = ng.Rows.FirstOrDefault(r => r.ClassName == className);
                if (row != null)
                {
                    row.CronDef = cronDef;
                    row.Enabled = enabled;
                }
                return View("Index", ng);
            }

            if (def == null)
            {
                def = new SchedulerDef { ClassName = className, CronDef = cronDef, Enabled = enabled };
                _context.SchedulerDefs.Add(def);
            }
            else
            {
                def.CronDef = cronDef;
                def.Enabled = enabled;
            }
            _context.SaveChanges();

            await RootScheduler.ApplyAsync(def);

            SchedulerModel model = await BuildModelAsync(fromAdmin);
            model.Message = BuildMessage(model, className, info?.DisplayName ?? className, enabled);
            return View("Index", model);
        }

        private static string? Validate(string className, string cronDef, bool enabled, SchedulerDef? def, JobInfo? info)
        {
            if (className.Length == 0 || (def == null && info == null))
            {
                return "ジョブが特定できませんでした。";
            }
            if (cronDef.Length == 0)
            {
                return "cron式を入力してください。";
            }
            if (cronDef.Length > 100)
            {
                return "cron式が長すぎます。100文字までです。";
            }
            if (!CronExpression.IsValidExpression(cronDef))
            {
                return $"cron式が正しくありません。（{cronDef}）";
            }
            //クラスが無いものを有効にしてもQuartzに登録できない。無効にするのは通す。
            if (enabled && info == null)
            {
                return $"クラス {className} が見つからないため有効にできません。";
            }
            return null;
        }

        private static string BuildMessage(SchedulerModel model, string className, string displayName, bool enabled)
        {
            SchedulerRow? row = model.Rows.FirstOrDefault(r => r.ClassName == className);

            if (!enabled)
            {
                string message = $"{displayName} を無効にしました。次からは実行されません。";
                //Quartzは動いているジョブを止められないので、待っても終わらないと誤解しないように書いておく
                return row is { Running: true } ? message + "　いま実行中の処理は最後まで走ります。" : message;
            }

            string next = row?.NextFireTime?.ToString("yyyy/MM/dd HH:mm:ss") ?? "―";
            return $"{displayName} を有効にしました。次回の実行は {next} です。";
        }

        private async Task<SchedulerModel> BuildModelAsync(bool fromAdmin)
        {
            Dictionary<string, SchedulerDef> defs = _context.SchedulerDefs.AsNoTracking()
                .ToDictionary(d => d.ClassName, StringComparer.Ordinal);
            IReadOnlyDictionary<string, JobRuntimeState> states = await RootScheduler.GetStatesAsync();

            List<SchedulerRow> rows = [];
            foreach (JobInfo info in JobCatalog.All)
            {
                defs.TryGetValue(info.ClassName, out SchedulerDef? def);
                rows.Add(BuildRow(info.ClassName, info, def, states));
            }

            //クラスが無くなった定義も、無効にしたり式を直したりできるように出す
            foreach (SchedulerDef def in defs.Values.Where(d => JobCatalog.Find(d.ClassName) == null))
            {
                rows.Add(BuildRow(def.ClassName, null, def, states));
            }

            return new SchedulerModel
            {
                Title = TITLE,
                IsAdmin = true,
                FromAdmin = fromAdmin,
                Rows = rows,
            };
        }

        private static SchedulerRow BuildRow(string className, JobInfo? info, SchedulerDef? def, IReadOnlyDictionary<string, JobRuntimeState> states)
        {
            states.TryGetValue(className, out JobRuntimeState? state);

            return new SchedulerRow
            {
                ClassName = className,
                DisplayName = info?.DisplayName ?? className,
                Description = info?.Description,
                CronDef = def?.CronDef ?? string.Empty,
                Enabled = def?.Enabled ?? false,
                Defined = def != null,
                TypeFound = info != null,
                Scheduled = state?.Scheduled ?? false,
                Running = state?.Running ?? false,
                NextFireTime = state?.NextFireTime,
                PreviousFireTime = state?.PreviousFireTime,
            };
        }
    }
}
