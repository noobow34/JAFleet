using System.Text.Json;
using JAFleet.Adsb;
using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using JAFleet.Models;
using JAFleet.Services;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Batch
{
    /// <summary>
    /// adsb.lol の履歴ダンプから作ったフライト（adsb_flight）で working_status を更新し、
    /// FR24 の巡回（RefreshWorkingStatusAndPhoto）と同じ形の稼働チェックログと通知を残す。
    /// 区分（稼働・非稼働・整備入り…）の決め方も FR24 のときと同じにしてある。
    /// </summary>
    public static class AdsbWorkingStatus
    {
        /// <summary>最後に飛んだ日がデータの締め（翌 9:00）の日からこの日数以内なら稼働。FR24 のときと同じ2日</summary>
        public const int WORKING_DAYS = 2;

        /// <summary>
        /// 整備拠点の判定で、到着地が決まらなかった便の最後の位置を拠点の近くとみる距離（km）と高度（ft）。
        /// 整備で着いた空港は接地まで受信できないこともあるので、降下中に近くで切れたものも拾う
        /// </summary>
        public const double MAINTE_NEAR_KM = 150;
        public const int MAINTE_NEAR_ALTITUDE = 10000;

        /// <summary>整備入りを見るとき、最後の便をさかのぼって探す日数</summary>
        public const int LAST_FLIGHT_LOOKBACK_DAYS = 14;

        /// <summary>
        /// dataDate（UTC）の取り込みが終わったあとに呼ぶ。SaveChanges まで行い、Slack に流す通知の内容を返す。
        /// testFlights は、テストレジ（JA 登録前の外国レジ）で飛んだ機体の最後の離陸時刻（日本時間）。
        /// </summary>
        public static async Task<WorkingStatusNotifyJson> UpdateAsync(JAFleetContext context, DateOnly dataDate,
            IReadOnlyDictionary<string, DateTime> testFlights, DateTime startedAt, CancellationToken ct = default)
        {
            var aircrafts = await context.AircraftViews.AsNoTracking()
                .Where(a => a.OperationCode != OperationCode.RETIRE_UNREGISTERED)
                .ToListAsync(ct);
            var statuses = await context.WorkingStatuses.ToDictionaryAsync(s => s.RegistrationNumber, StringComparer.Ordinal, ct);
            var from = dataDate.AddDays(-LAST_FLIGHT_LOOKBACK_DAYS);
            var recent = await context.AdsbFlights.AsNoTracking()
                .Where(f => f.DataDate >= from && f.DataDate <= dataDate)
                .ToListAsync(ct);
            var maintePlaces = await context.Airports.AsNoTracking()
                .Where(a => a.Iata != null && RefreshWorkingStatusAndPhoto.MAINTE_PLACE.Contains(a.Iata))
                .ToListAsync(ct);

            var todayByReg = recent.Where(f => f.DataDate == dataDate)
                .GroupBy(f => f.RegistrationNumber)
                .ToDictionary(g => g.Key, g => g.OrderBy(f => f.DepartureTime).Last(), StringComparer.Ordinal);
            var lastByReg = recent
                .GroupBy(f => f.RegistrationNumber)
                .ToDictionary(g => g.Key, g => g.OrderBy(f => f.ArrivalTime).Last(), StringComparer.Ordinal);

            var now = DateTime.Now;
            var sections = new WorkingStatusSections();
            //データの締めは翌日の 9:00（日本時間）。その日付を「今日」として、FR24 のときと同じ日数で稼働を決める
            var referenceDate = dataDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
            foreach (var a in aircrafts.OrderBy(a => a.RegistrationNumber, StringComparer.Ordinal))
            {
                string reg = a.RegistrationNumber!;
                if (!statuses.TryGetValue(reg, out var status))
                {
                    status = new WorkingStatus { RegistrationNumber = reg };
                    context.WorkingStatuses.Add(status);
                    statuses[reg] = status;
                }
                Apply(a, status, todayByReg.GetValueOrDefault(reg), lastByReg.GetValueOrDefault(reg),
                    referenceDate, maintePlaces, sections, now);
                if (testFlights.TryGetValue(reg, out DateTime testDate))
                {
                    ApplyTestFlight(a, status, testDate, sections);
                }
            }

            var sources = sections.ToSources();
            context.Logs.Add(new Log
            {
                LogDate = now,
                LogType = LogType.WORKING_INFO,
                LogDetail = JsonSerializer.Serialize(new WorkingCheckLogJson
                {
                    FinishedAt = now.ToString("yyyy/MM/dd HH:mm:ss"),
                    Sections = RefreshWorkingStatusAndPhoto.BuildSections(sources),
                    Source = WorkingStatusUpdateInfo.SOURCE_ADSB,
                    DataDate = dataDate.ToString("yyyy-MM-dd"),
                }),
            });
            var notify = new WorkingStatusNotifyJson
            {
                FinishedAt = now.ToString("yyyy/MM/dd HH:mm:ss"),
                Elapsed = (now - startedAt).ToString(@"hh\:mm\:ss"),
                WaitSeconds = 0,
                LogDate = now.ToString("yyyyMMdd"),
                Sections = RefreshWorkingStatusAndPhoto.BuildNotifySections(sources),
                Source = $"adsb.lol {dataDate:yyyy-MM-dd} UTC",
            };
            context.Logs.Add(new Log
            {
                LogDate = now,
                LogType = LogType.WORKING_NOTIFY_TEXT,
                LogDetail = JsonSerializer.Serialize(notify),
            });
            await context.SaveChangesAsync(ct);
            return notify;
        }

        /// <summary>
        /// 1機分の working_status を更新し、変化を区分に積む。
        /// today はデータ日に飛んだ最後の便（無ければ null）、last はさかのぼって最後の便（整備拠点の判定用）。
        /// </summary>
        public static void Apply(AircraftView a, WorkingStatus status, AdsbFlight? today, AdsbFlight? last,
            DateTime referenceDate, IReadOnlyList<Airport> maintePlaces, WorkingStatusSections sections, DateTime now)
        {
            string reg = a.RegistrationNumber!;
            bool? previousWorking = status.Working;
            DateTime? previousDate = status.FlightDate;

            //流し直しで古い日を入れたときに、新しい便を古い便で上書きしない
            if (today != null && (status.FlightDate == null || today.DepartureTime >= status.FlightDate))
            {
                status.FlightDate = today.DepartureTime;
                //working_status の文字列の列はどれも varchar(30)。1件のはみ出しで全機の更新が止まらないよう切り詰める
                status.FromAp = Fit(today.FromAp ?? "—");
                status.ToAp = Fit(today.ToAp ?? "—");
                status.FlightNumber = Fit(today.FlightNumber ?? "—");
                status.Status = Fit(today.Status);
                status.ExistOperation = true;
            }
            status.Working = status.FlightDate is DateTime d && (referenceDate.Date - d.Date).TotalDays <= WORKING_DAYS;
            status.UpdatedAt = now;

            var entry = Entry(a, status);
            if (previousWorking != true && status.Working == true)
            {
                //非稼働 → 稼働
                var withPrev = entry with { PreviousDate = previousDate?.ToString("yyyy/MM/dd HH:mm") };
                sections.AddToWorking(a.OperationCode, reg, withPrev);
                if (status.Maintenancing == true)
                {
                    status.Maintenancing = false;
                    sections.MainteEnd.Add(reg, entry);
                }
            }
            else if (previousWorking == true && status.Working == false)
            {
                //稼働 → 非稼働。最後の便の行き先（または最後の位置）が整備拠点なら整備入り
                sections.ToNotWorking.Add(reg, entry);
                if (IsMaintenancePlace(last, status.ToAp, maintePlaces))
                {
                    status.Maintenancing = true;
                    sections.MainteStart.Add(reg, entry);
                }
            }
            else if (status.Maintenancing == true && a.OperationCode != OperationCode.RETIRE_REGISTERED)
            {
                sections.Mainteing.Add(reg, entry);
            }
        }

        /// <summary>working_status の文字列の列（varchar(30)）に収まるよう切り詰める</summary>
        public const int WORKING_STATUS_TEXT_MAX = 30;

        public static string? Fit(string? text)
            => text == null || text.Length <= WORKING_STATUS_TEXT_MAX ? text : text[..(WORKING_STATUS_TEXT_MAX - 1)] + "…";

        /// <summary>テストレジで前回より新しく飛んでいたら「テストレジが稼働」に積む</summary>
        public static void ApplyTestFlight(AircraftView a, WorkingStatus status, DateTime testDate, WorkingStatusSections sections)
        {
            if (status.TestFlightDate.HasValue && status.TestFlightDate.Value >= testDate)
            {
                return;
            }
            sections.ToWorkingTest[a.RegistrationNumber!] = new WorkingCheckLogEntryJson
            {
                Reg = a.RegistrationNumber,
                Type = a.TypeDetailName,
                Date = testDate.ToString("yyyy/MM/dd HH:mm"),
                PreviousDate = status.TestFlightDate?.ToString("yyyy/MM/dd HH:mm"),
                Mark = $"test:{a.TestRegistration}",
            };
            status.TestFlightDate = testDate;
        }

        /// <summary>
        /// 整備拠点に着いたか。到着地の表示（FR24 と同じく IATA が入る）で見て、到着地が決まらなかった便は
        /// 最後の位置が拠点の近く（降下中・低空）かで見る。adsb の便が無ければ今の到着地の表示だけで見る。
        /// </summary>
        public static bool IsMaintenancePlace(AdsbFlight? last, string? toAp, IReadOnlyList<Airport> maintePlaces)
        {
            string? arrival = last?.ToAp ?? toAp;
            if (arrival != null && RefreshWorkingStatusAndPhoto.MAINTE_PLACE.Any(m => arrival.Contains(m)))
            {
                return true;
            }
            if (last == null || last.ToCode != null || last.EndAltitude is > MAINTE_NEAR_ALTITUDE)
            {
                return false;
            }
            return maintePlaces.Any(p => Geo.DistanceKm(last.EndLat, last.EndLon, p.Latitude, p.Longitude) <= MAINTE_NEAR_KM);
        }

        private static WorkingCheckLogEntryJson Entry(AircraftView a, WorkingStatus status) => new()
        {
            Reg = a.RegistrationNumber,
            Mark = !string.IsNullOrEmpty(a.SpecialLivery) ? "◎" : a.MaintenanceNotify == true ? "☆" : string.Empty,
            Type = a.TypeDetailName,
            Date = status.FlightDate?.ToString("yyyy/MM/dd HH:mm"),
            FromAp = status.FromAp,
            ToAp = status.ToAp,
            FlightNumber = status.FlightNumber,
            Status = status.Status,
        };
    }

    /// <summary>稼働チェックの区分ごとの一覧（FR24 の巡回と同じ区分・同じ順）</summary>
    public class WorkingStatusSections
    {
        public SortedDictionary<string, WorkingCheckLogEntryJson> ToWorkingTest { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, WorkingCheckLogEntryJson> ToWorking0 { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, WorkingCheckLogEntryJson> ToWorking1 { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, WorkingCheckLogEntryJson> ToWorking2 { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, WorkingCheckLogEntryJson> ToWorking3 { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, WorkingCheckLogEntryJson> ToWorking7 { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, WorkingCheckLogEntryJson> ToNotWorking { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, WorkingCheckLogEntryJson> MainteStart { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, WorkingCheckLogEntryJson> MainteEnd { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, WorkingCheckLogEntryJson> Mainteing { get; } = new(StringComparer.Ordinal);

        public void AddToWorking(string? operationCode, string reg, WorkingCheckLogEntryJson entry)
        {
            var target = operationCode switch
            {
                OperationCode.RESERVED => ToWorking0,
                OperationCode.MAKING => ToWorking1,
                OperationCode.DELIVERY => ToWorking2,
                OperationCode.INTERNATIONAL or OperationCode.DOMESTIC or OperationCode.BOTH or OperationCode.CARGO => ToWorking3,
                OperationCode.RETIRE_REGISTERED => ToWorking7,
                _ => null,
            };
            target?.Add(reg, entry);
        }

        public (string title, SortedDictionary<string, WorkingCheckLogEntryJson> dict)[] ToSources() =>
        [
            ("テストレジが稼働", ToWorkingTest),
            ("予約登録が稼働", ToWorking0),
            ("製造中が稼働", ToWorking1),
            ("デリバリーが稼働", ToWorking2),
            ("運用中非稼働が稼働", ToWorking3),
            ("退役未抹消が稼働", ToWorking7),
            ("稼働が非稼働", ToNotWorking),
            ("整備入り", MainteStart),
            ("整備終了", MainteEnd),
            ("整備中", Mainteing),
        ];
    }
}
