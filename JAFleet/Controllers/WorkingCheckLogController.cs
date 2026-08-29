using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using JAFleet.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace JAFleet.Controllers
{
    public class WorkingCheckLogController : Controller
    {
        private readonly JAFleetContext _context;

        public WorkingCheckLogController(JAFleetContext context) => _context = context;

        public IActionResult Index(string id, [FromQuery] bool fromAdmin)
        {
            DateTime searchDate;
            if (string.IsNullOrEmpty(id))
            {
                searchDate = DateTime.Now.Date;
            }
            else
            {
                if (!DateTime.TryParseExact(id, "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out searchDate))
                {
                    searchDate = DateTime.Now.Date;
                }
            }

            var rawLogs = _context.Logs
                .Where(l => l.LogDate!.Value.Date == searchDate && l.LogType == LogType.WORKING_INFO)
                .AsNoTracking()
                .OrderByDescending(l => l.LogDate)
                .Select(l => l.LogDetail)
                .ToList();

            var model = new WorkingCheckLogModel
            {
                Title      = $"稼働チェックログ {searchDate:yyyy/MM/dd}",
                SearchDate = searchDate,
                Batches    = rawLogs.Select(ToViewModel).ToList(),
                FromAdmin  = fromAdmin,
            };

            return View(model);
        }

        /// <summary>LogDetailのJSON文字列をViewModelに変換</summary>
        private static WorkingCheckLogBatch ToViewModel(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new WorkingCheckLogBatch();

            WorkingCheckLogJson? logJson;
            try
            {
                logJson = JsonSerializer.Deserialize<WorkingCheckLogJson>(json);
            }
            catch
            {
                // 旧形式（非JSON）が残っていた場合のフォールバック
                return new WorkingCheckLogBatch
                {
                    Sections = new List<WorkingCheckLogSection>
                    {
                        new() { SectionTitle = "（旧形式ログ）", Entries = new List<WorkingCheckLogEntry>
                            {
                                new() { RawLine = json }
                            }
                        }
                    }
                };
            }

            if (logJson == null) return new WorkingCheckLogBatch();

            return new WorkingCheckLogBatch
            {
                FinishedAt = logJson.FinishedAt,
                Sections   = logJson.Sections.Select(s => new WorkingCheckLogSection
                {
                    SectionTitle = s.Title,
                    Entries      = s.Entries.Select(ToEntry).ToList()
                }).ToList()
            };
        }

        private static WorkingCheckLogEntry ToEntry(WorkingCheckLogEntryJson e)
        {
            var (fromCode, fromName) = SplitAp(e.FromAp);
            var (toCode,   toName)   = SplitAp(e.ToAp);
            return new WorkingCheckLogEntry
            {
                RegistrationNumber = e.Reg,
                NotifyMark         = e.Mark,
                TypeName           = e.Type,
                FlightDate         = e.Date,
                FromAp             = e.FromAp,
                FromApCode         = fromCode,
                FromApName         = fromName,
                ToAp               = e.ToAp,
                ToApCode           = toCode,
                ToApName           = toName,
                FlightNumber       = e.FlightNumber,
                Status             = e.Status,
                PreviousDate       = e.PreviousDate,
            };
        }

        /// <summary>"Tokyo (NRT)" → ("NRT", "Tokyo")、"—" → ("—", "")</summary>
        private static (string code, string name) SplitAp(string? ap)
        {
            if (string.IsNullOrEmpty(ap)) return ("", "");
            if (ap == "—") return ("—", "");
            var idx = ap.LastIndexOf('(');
            if (idx < 0) return (ap, "");
            return (ap[(idx + 1)..].TrimEnd(')'), ap[..idx].Trim());
        }
    }
}
