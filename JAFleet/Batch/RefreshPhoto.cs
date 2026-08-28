using AngleSharp;
using EnumStringValues;
using JAFleet.Commons.Scraping;
using JAFleet.Commons.Constants;
using JAFleet.Commons.EF;
using Microsoft.EntityFrameworkCore;
using Noobow.Commons.Constants;
using Noobow.Commons.Extensions;
using Noobow.Commons.Utils;
using System.Text;

namespace JAFleet.Batch
{
    public class RefreshPhoto
    {
        private IEnumerable<AircraftView> _targetRegistrationNumber;
        private int _interval;
        public static DbContextOptionsBuilder<JAFleetContext>? Options { get; set; }
        public static bool Processing { get; set; } = false;

        public RefreshPhoto(IEnumerable<AircraftView> targetRegistrationNumber, int interval)
        {
            _targetRegistrationNumber = targetRegistrationNumber;
            _interval = interval;
            if (Options == null)
            {
                Options = new DbContextOptionsBuilder<JAFleetContext>();
                Options.UseNpgsql(Environment.GetEnvironmentVariable("JAFLEET_CONNECTION_STRING") ?? "");
            }
        }

        public async Task ExecuteRefreshAsync()
        {
            Processing = true;
            var r = new Random();
            using var context = new JAFleetContext(Options!.Options);
            AngleSharp.IConfiguration? _config = Configuration.Default.WithDefaultLoader().WithDefaultCookies().WithXPath();
            IBrowsingContext _context = BrowsingContext.New(_config);
            foreach (var a in _targetRegistrationNumber) {
                bool success = false;
                int failCount = 0;
                Exception? exBack = null;
                StringBuilder logLine = new();
                while (!success && failCount <= 5)
                {
                    try
                    {
                        string url = $"{FlightradarConstant.FR24_DATA_URL}{a.RegistrationNumber}";
                        var doc = await _context.OpenAsync(url);
                        var row = doc?.Body?.GetElementsByClassName("data-row");
                        var ap = AircraftDataExtractor.ExtractPhotoDataFromJetphotos(doc);

                        AircraftPhoto? photo = context.AircraftPhotos.Where(p => p.RegistrationNumber == a.RegistrationNumber).SingleOrDefault();
                        WorkingStatus? ws = context.WorkingStatuses.Where(ws => ws.RegistrationNumber == a.RegistrationNumber).SingleOrDefault();
                        //写真が登録されているが今回取得できなかった場合はスキップ
                        if (ap != null)
                        {
                            logLine.Append($"{a.RegistrationNumber}:写真取得");
                            if (ws != null)
                            {
                                ws.ExistPage = true;
                                ws.UpdatedAt = DateTime.Now;
                            }
                            if (photo != null)
                            {
                                photo.PhotoUrl = ap.PhotoUrl;
                                photo.PhotoDirectLarge = ap.PhotoDirectLarge;
                                photo.PhotoDirectSmall = ap.PhotoDirectSmall;
                                photo.LastAccess = DateTime.Now;
                            }
                            else
                            {
                                photo = new AircraftPhoto()
                                {
                                    RegistrationNumber = a.RegistrationNumber,
                                    PhotoUrl = ap!.PhotoUrl,
                                    PhotoDirectLarge = ap.PhotoDirectLarge,
                                    PhotoDirectSmall = ap.PhotoDirectSmall,
                                    LastAccess = DateTime.Now
                                };
                                context.AircraftPhotos.Add(photo);
                            }
                        }
                        else
                        {
                            logLine.Append($"{a.RegistrationNumber}:写真なし");
                            if (ws != null)
                            {
                                ws.ExistPage = false;
                                ws.UpdatedAt = DateTime.Now;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        exBack = ex;
                        Thread.Sleep(60 * 1000); //Exceptionになったら1分待機
                    }
                    success = true;
                }
                if (failCount > 5)
                {
                    Console.WriteLine(exBack?.ToString());
                    await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), $"RefreshPhoto異常終了:{DateTime.Now}\n");
                    await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), exBack!.ToString());
                    Processing = false;
                    return;
                }

                int interval = Convert.ToInt32(r.NextDouble() * _interval * 1000);
                logLine.Append($"、{interval}ミリ秒待機");
                this.JournalWriteLine(logLine.ToString());
                Thread.Sleep(interval);
            }
            context.SaveChanges();
            Processing = false;
            this.JournalWriteLine("RefreshPhoto正常終了");
        }
    }
}
