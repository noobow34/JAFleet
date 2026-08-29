using AngleSharp;
using AngleSharp.Html.Parser;
using EnumStringValues;
using JAFleet.Commons.Scraping;
using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using JAFleet.Models;
using Microsoft.EntityFrameworkCore;
using Noobow.Commons.Constants;
using Noobow.Commons.Extensions;
using Noobow.Commons.Utils;
using System.Diagnostics;
using System.Text.Json;

namespace JAFleet.Batch
{
    public class RefreshWorkingStatusAndPhoto
    {
        private IEnumerable<AircraftView> _targetRegistrationNumber;
        private int _interval;
        private readonly static TimeSpan CompareTargetTimeSpan = new(2, 0, 0, 0);
        private static readonly string[] MAINTE_PLACE = ["TPE", "MNL", "XSP", "QPG", "XMN", "SIN", "TNA", "HKG", "OKA", "TNN"];
        public static DbContextOptionsBuilder<JAFleetContext>? Options { get; set; }
        public static bool Processing { get; set; } = false;

        public RefreshWorkingStatusAndPhoto(IEnumerable<AircraftView> targetRegistrationNumber, int interval)
        {
            _targetRegistrationNumber = targetRegistrationNumber;
            _interval = interval;
            if (Options == null)
            {
                Options = new DbContextOptionsBuilder<JAFleetContext>();
                Options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            }
        }

        public async Task ExecuteCheckAsync(bool needsNotify = false)
        {
            int intervalSum = 0;
            var sw = new Stopwatch();
            sw.Start();
            Processing = true;
            var parser = new HtmlParser();

            using var context = new JAFleetContext(Options!.Options);

            // SortedDictionary の値を文字列からJSON用レコードに変更
            var toWorkingTest = new SortedDictionary<string, WorkingCheckLogEntryJson>();
            var toWorking0    = new SortedDictionary<string, WorkingCheckLogEntryJson>();
            var toWorking1    = new SortedDictionary<string, WorkingCheckLogEntryJson>();
            var toWorking2    = new SortedDictionary<string, WorkingCheckLogEntryJson>();
            var toWorking3    = new SortedDictionary<string, WorkingCheckLogEntryJson>();
            var toWorking7    = new SortedDictionary<string, WorkingCheckLogEntryJson>();
            var toNotWorking  = new SortedDictionary<string, WorkingCheckLogEntryJson>();
            var mainteStart   = new SortedDictionary<string, WorkingCheckLogEntryJson>();
            var mainteEnd     = new SortedDictionary<string, WorkingCheckLogEntryJson>();
            var mainteing     = new SortedDictionary<string, WorkingCheckLogEntryJson>();

            AngleSharp.IConfiguration? _config = Configuration.Default.WithDefaultLoader().WithDefaultCookies().WithXPath();
            IBrowsingContext _context = BrowsingContext.New(_config);
            var r = new Random();

            foreach (AircraftView a in _targetRegistrationNumber)
            {
                bool success = false;
                int failCount = 0;
                Exception? exBack = null;
                bool existPhoto = false;
                bool existPage = false;
                bool existOperation = false;

                while (!success && failCount <= 5)
                {
                    try
                    {
                        string url = $"{FlightradarConstant.FR24_DATA_URL}{a.RegistrationNumber}";
                        var doc = await _context.OpenAsync(url);
                        var row = doc?.Body?.GetElementsByClassName("data-row");
                        var ap = AircraftDataExtractor.ExtractPhotoDataFromJetphotos(doc);

                        existPage = doc?.BaseUri.Contains(a.RegistrationNumber, StringComparison.CurrentCultureIgnoreCase) ?? false;

                        string notifyMark = string.Empty;
                        if (!string.IsNullOrEmpty(a.SpecialLivery))
                            notifyMark = "◎";
                        else if (a.MaintenanceNotify.HasValue && a.MaintenanceNotify.Value)
                            notifyMark = "☆";

                        // 写真更新処理（変更なし）
                        AircraftPhoto? photo = context.AircraftPhotos
                            .Where(p => p.RegistrationNumber == a.RegistrationNumber)
                            .FirstOrDefault();
                        if (ap != null)
                        {
                            existPhoto = true;
                            if (photo != null)
                            {
                                photo.PhotoUrl         = ap.PhotoUrl;
                                photo.PhotoDirectLarge = ap.PhotoDirectLarge;
                                photo.PhotoDirectSmall = ap.PhotoDirectSmall;
                                photo.LastAccess       = DateTime.Now;
                            }
                            else
                            {
                                photo = new AircraftPhoto()
                                {
                                    RegistrationNumber = a.RegistrationNumber,
                                    PhotoUrl           = ap.PhotoUrl,
                                    PhotoDirectLarge   = ap.PhotoDirectLarge,
                                    PhotoDirectSmall   = ap.PhotoDirectSmall,
                                    LastAccess         = DateTime.Now
                                };
                                context.AircraftPhotos.Add(photo);
                            }
                        }

                        var status = context.WorkingStatuses
                            .Where(s => s.RegistrationNumber == a.RegistrationNumber)
                            .FirstOrDefault();

                        if (row!.Length != 0)
                        {
                            existOperation = true;

                            string? timestamp = row[0].GetAttribute("data-timestamp");
                            DateTime latestDate = DateTimeOffset.FromUnixTimeSeconds(long.Parse(timestamp!)).LocalDateTime;

                            var td = row[0].GetElementsByTagName("td");
                            string fromAp      = td!.Length != 0 ? td[3].TextContent!.Trim() : string.Empty;
                            string toAp        = td!.Length != 0 ? td[4].TextContent!.Trim() : string.Empty;
                            string flightNum   = td!.Length != 0 ? td[5].TextContent!.Trim() : string.Empty;
                            string flightStatus = td!.Length != 0 ? td[11].TextContent!.Trim() : string.Empty;

                            // 現在フライト情報のエントリを構築
                            var currentEntry = new WorkingCheckLogEntryJson
                            {
                                Reg          = a.RegistrationNumber,
                                Mark         = notifyMark,
                                Type         = a.TypeDetailName,
                                Date         = latestDate.ToString("yyyy/MM/dd HH:mm"),
                                FromAp       = fromAp,
                                ToAp         = toAp,
                                FlightNumber = flightNum,
                                Status       = flightStatus,
                            };

                            if (status == null)
                            {
                                status = new WorkingStatus() { RegistrationNumber = a.RegistrationNumber };
                                context.WorkingStatuses.Add(status);
                            }

                            bool? previousWorking = status.Working;
                            DateTime? previousDate = status.FlightDate;

                            status.FlightDate     = latestDate;
                            status.FromAp         = fromAp;
                            status.ToAp           = toAp;
                            status.FlightNumber   = flightNum;
                            status.Status         = flightStatus;
                            status.Working        = (DateTime.Now.Date < latestDate.Date) || ((DateTime.Now.Date - latestDate.Date) <= CompareTargetTimeSpan);
                            status.ExistPage      = existPage;
                            status.ExistPhoto     = existPhoto;
                            status.ExistOperation = existOperation;
                            status.UpdatedAt      = DateTime.Now;

                            if ((!previousWorking.HasValue || !previousWorking.Value) && status.Working!.Value)
                            {
                                // 非稼働 → 稼働
                                var entryWithPrev = currentEntry with
                                {
                                    PreviousDate = previousDate.HasValue ? previousDate.Value.ToString("yyyy/MM/dd HH:mm") : null
                                };

                                switch (a.OperationCode)
                                {
                                    case OperationCode.RESERVED:          toWorking0.Add(a.RegistrationNumber, entryWithPrev); break;
                                    case OperationCode.MAKING:            toWorking1.Add(a.RegistrationNumber, entryWithPrev); break;
                                    case OperationCode.DELIVERY:          toWorking2.Add(a.RegistrationNumber, entryWithPrev); break;
                                    case OperationCode.INTERNATIONAL:
                                    case OperationCode.DOMESTIC:
                                    case OperationCode.BOTH:
                                    case OperationCode.CARGO:             toWorking3.Add(a.RegistrationNumber, entryWithPrev); break;
                                    case OperationCode.RETIRE_REGISTERED: toWorking7.Add(a.RegistrationNumber, entryWithPrev); break;
                                }

                                // 整備終了の疑い
                                if (status.Maintenancing.HasValue && status.Maintenancing.Value)
                                {
                                    status.Maintenancing = false;
                                    mainteEnd.Add(a.RegistrationNumber, currentEntry);
                                }
                            }
                            else if (previousWorking.HasValue && previousWorking.Value && !status.Working.Value)
                            {
                                // 稼働 → 非稼働
                                toNotWorking.Add(a.RegistrationNumber, currentEntry);

                                // 整備開始の疑い
                                if (MAINTE_PLACE.Any(m => status.ToAp!.Contains(m)))
                                {
                                    status.Maintenancing = true;
                                    mainteStart.Add(a.RegistrationNumber, currentEntry);
                                }
                            }
                            else if (status.Maintenancing.HasValue && status.Maintenancing.Value
                                     && a.OperationCode != OperationCode.RETIRE_REGISTERED)
                            {
                                mainteing.Add(a.RegistrationNumber, currentEntry);
                            }
                        }
                        else
                        {
                            // FR24にデータなし
                            if (status != null)
                            {
                                var oldEntry = new WorkingCheckLogEntryJson
                                {
                                    Reg          = status.RegistrationNumber,
                                    Type         = a.TypeDetailName,
                                    Date         = status.FlightDate?.ToString("yyyy/MM/dd HH:mm"),
                                    FromAp       = status.FromAp,
                                    ToAp         = status.ToAp,
                                    FlightNumber = status.FlightNumber,
                                    Status       = status.Status,
                                };

                                if (status.Working.HasValue && status.Working.Value)
                                    toNotWorking.Add(a.RegistrationNumber, oldEntry);

                                if (status.Maintenancing.HasValue && status.Maintenancing.Value
                                    && a.OperationCode != OperationCode.RETIRE_REGISTERED)
                                    mainteing.Add(a.RegistrationNumber, oldEntry);

                                status.Working        = false;
                                status.ExistPage      = existPage;
                                status.ExistPhoto     = existPhoto;
                                status.ExistOperation = existOperation;
                                status.UpdatedAt      = DateTime.Now;
                            }
                            else
                            {
                                status = new WorkingStatus()
                                {
                                    RegistrationNumber = a.RegistrationNumber,
                                    Working            = false,
                                    ExistPage          = existPage,
                                    ExistPhoto         = existPhoto,
                                    ExistOperation     = existOperation,
                                    UpdatedAt          = DateTime.Now
                                };
                                context.WorkingStatuses.Add(status);
                            }
                        }

                        int interval = Convert.ToInt32(r.NextDouble() * _interval * 1000);
                        intervalSum += interval;

                        // ジャーナルログ（画面確認用の1行テキストは残す）
                        this.JournalWriteLine(
                            $"{a.RegistrationNumber}({a.TypeDetailName})" +
                            $":{existPage}:{existPhoto}:{existOperation}:{interval}ミリ秒待機");
                        Thread.Sleep(interval);

                        // テストレジのチェック（変更なし）
                        if (!string.IsNullOrEmpty(a.TestRegistration))
                        {
                            var testRegurl = FlightradarConstant.FR24_DATA_URL + a.TestRegistration;
                            var testDoc = await _context.OpenAsync(testRegurl);
                            var rowTest = doc?.Body?.GetElementsByClassName("data-row");
                            if (rowTest!.Length != 0)
                            {
                                string? timestamp = rowTest[0].GetAttribute("data-timestamp");
                                DateTime latestDate = DateTimeOffset.FromUnixTimeSeconds(long.Parse(timestamp!)).LocalDateTime;
                                if (!status!.TestFlightDate.HasValue || status.TestFlightDate.Value < latestDate)
                                {
                                    var testEntry = new WorkingCheckLogEntryJson
                                    {
                                        Reg          = a.RegistrationNumber,
                                        Type         = a.TypeDetailName,
                                        Date         = latestDate.ToString("yyyy/MM/dd HH:mm"),
                                        PreviousDate = status.TestFlightDate.HasValue
                                                       ? status.TestFlightDate.Value.ToString("yyyy/MM/dd HH:mm")
                                                       : null,
                                        // テストレジ番号はMarkフィールドに格納
                                        Mark         = $"test:{a.TestRegistration}",
                                    };
                                    status.TestFlightDate = latestDate;
                                    toWorkingTest.Add(a.RegistrationNumber, testEntry);
                                }
                            }
                            interval = Convert.ToInt32(r.NextDouble() * _interval * 1000);
                            intervalSum += interval;
                            this.JournalWriteLine($"テストレジあり:{interval}ミリ秒待機");
                            Thread.Sleep(interval);
                        }

                        success = true;
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        exBack = ex;
                        Thread.Sleep(60 * 1000);
                    }
                }

                if (failCount > 5)
                {
                    this.JournalWriteLine(exBack?.ToString() ?? string.Empty);
                    await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), $"RefreshWorkingStatus異常終了:{DateTime.Now}\n");
                    await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), exBack!.ToString());
                    Processing = false;
                    return;
                }
            }

            // ======== JSON組み立て ========
            (string title, SortedDictionary<string, WorkingCheckLogEntryJson> dict)[] sectionSources =
            [
                ("テストレジが稼働",   toWorkingTest),
                ("予約登録が稼働",     toWorking0),
                ("製造中が稼働",       toWorking1),
                ("デリバリーが稼働",   toWorking2),
                ("運用中非稼働が稼働", toWorking3),
                ("退役未抹消が稼働",   toWorking7),
                ("稼働が非稼働",       toNotWorking),
                ("整備入り",           mainteStart),
                ("整備終了",           mainteEnd),
                ("整備中",             mainteing)
            ];

            var logJson = new WorkingCheckLogJson
            {
                FinishedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"),
                Sections   = BuildSections(sectionSources)
            };

            var workingCheckLog = new Log
            {
                LogDate   = DateTime.Now,
                LogType   = LogType.WORKING_INFO,
                LogDetail = JsonSerializer.Serialize(logJson),
            };
            context.Logs.Add(workingCheckLog);

            // ======== Slack通知（BlockKit） ========
            sw.Stop();
            var notify = new WorkingStatusNotifyJson
            {
                FinishedAt  = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"),
                Elapsed     = sw.Elapsed.ToString(),
                WaitSeconds = intervalSum / 1000.0,
                LogDate     = DateTime.Now.ToString("yyyyMMdd"),
                Sections    = BuildNotifySections(sectionSources)
            };

            if (needsNotify)
                await SlackUtil.PostAsync(
                    SlackChannelEnum.jafleet.GetStringValue(),
                    WorkingStatusSlackMessage.BuildText(notify),
                    WorkingStatusSlackMessage.BuildBlocks(notify));

            var wcNotify = new Log
            {
                LogDate   = DateTime.Now,
                LogType   = LogType.WORKING_NOTIFY_TEXT,
                LogDetail = JsonSerializer.Serialize(notify),
            };
            context.Logs.Add(wcNotify);
            context.SaveChanges();

            Processing = false;
        }

        /// <summary>空でないセクションだけリスト化するヘルパー</summary>
        private static List<WorkingCheckLogSectionJson> BuildSections(
            params (string title, SortedDictionary<string, WorkingCheckLogEntryJson> dict)[] sections)
        {
            var result = new List<WorkingCheckLogSectionJson>();
            foreach (var (title, dict) in sections)
            {
                if (dict.Count > 0)
                    result.Add(new WorkingCheckLogSectionJson
                    {
                        Title   = title,
                        Entries = dict.Values.ToList()
                    });
            }
            return result;
        }

        /// <summary>Slack通知用に空でない区分だけをレジ一覧付きでリスト化するヘルパー</summary>
        private static List<WorkingStatusNotifySectionJson> BuildNotifySections(
            params (string title, SortedDictionary<string, WorkingCheckLogEntryJson> dict)[] sections)
        {
            var result = new List<WorkingStatusNotifySectionJson>();
            foreach (var (title, dict) in sections)
            {
                if (dict.Count == 0)
                    continue;

                result.Add(new WorkingStatusNotifySectionJson
                {
                    Title = title,
                    Regs  = dict.Values.Select(e => new WorkingStatusNotifyRegJson
                    {
                        Reg  = e.Reg ?? string.Empty,
                        // テストレジ区分のMarkは"test:レジ"なので、特別塗装・整備通知のマークだけ表示する
                        Mark = e.Mark is "◎" or "☆" ? e.Mark : null,
                    }).ToList()
                });
            }
            return result;
        }
    }
}
