using jafleet.Commons.Constants;
using jafleet.Commons.EF;
using jafleet.Manager;
using jafleet.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace jafleet.Controllers
{
    public class logController : Controller
    {

        private readonly JafleetContext _context;

        public logController(JafleetContext context) => _context = context;

        public IActionResult Yesterday()
        {
            return Index("y");
        }

        [Authorize]
        public IActionResult Index(string id)
        {
            DateTime? targetDate = null;
            if (string.IsNullOrEmpty(id))
            {
                targetDate = DateTime.Now.Date;
            }
            else if (id.Length == 6)
            {
                id = "20" + id;
                DateTime.TryParseExact(id, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime outDate);
                targetDate = outDate;
            }
            else if (id == "y")
            {
                targetDate = DateTime.Now.AddDays(-1).Date;
            }

            List<Log>? logs = null;
            logs = _context.Logs.AsNoTracking().Where(q => q.LogDate!.Value.Date == targetDate!.Value.Date && !MasterManager.AdminUser!.Contains(q.UserId ?? string.Empty) && q.LogType != "8").OrderByDescending(q => q.LogId).ToList();

            var logScKeys = logs.Where(sc => sc.LogType == LogType.SEARCH).Select(scc => scc.LogDetail).Distinct();
            var scCache = GetSearchConditionDisps(logScKeys!);

            var entries = logs.Select(log => new LogEntry
            {
                LogDate     = log.LogDate,
                LogTypeCode = log.LogType,
                LogTypeName = LogType.GetLogTypeName(log.LogType!),
                UserId      = log.UserId,
                Detail      = log.LogType == LogType.SEARCH ? scCache[log.LogDetail!] + log.Additional : log.LogDetail,
            }).ToList();

            var model = new LogModel
            {
                Title      = $"アクセスログ {targetDate:yyyy/MM/dd}",
                SearchDate = targetDate!.Value,
                Entries    = entries,
            };

            return View(model);
        }

        public Dictionary<string, string> GetSearchConditionDisps(IEnumerable<string> scKeys)
        {
            var ret = new Dictionary<string, string>();
            var scs = _context.SearchConditions.AsNoTracking().Where(sc => scKeys.Contains(sc.SearchConditionKey)).ToList();
            foreach (var sc in scs)
            {
                string scJson;
                string searchConditionDisp = string.Empty;
                scJson = sc.SearchConditionJson ?? string.Empty;
                if (!string.IsNullOrEmpty(scJson) && scJson.Contains("TypeDetail"))
                {
                    var scm = JsonConvert.DeserializeObject<SearchConditionInModel>(scJson, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate })!;
                    var typeDetails = MasterManager.TypeDetailGroup?.Where(td => scm!.TypeDetail!.Split("|").ToList().Contains(td.TypeDetailId.ToString()));
                    scm.TypeDetail = string.Join("|", typeDetails!.Select(td => td.TypeDetailName));
                    searchConditionDisp = scm.ToString();
                }
                else
                {
                    searchConditionDisp = scJson;
                }
                ret.Add(sc.SearchConditionKey, searchConditionDisp);
            }
            return ret;
        }
    }
}