using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Services
{
    /// <summary>予約した項目1つ分。aircraft_reservation.changes にこれの配列をJSONで入れる</summary>
    public sealed class ReservedChange
    {
        /// <summary>Aircraftのプロパティ名</summary>
        [JsonPropertyName("field")]
        public string Field { get; set; } = string.Empty;

        /// <summary>予約した時点の値。画面に出すだけで、反映には使わない</summary>
        [JsonPropertyName("before")]
        public JsonElement Before { get; set; }

        /// <summary>反映する値</summary>
        [JsonPropertyName("after")]
        public JsonElement After { get; set; }
    }

    /// <summary>
    /// 機体情報の更新の予約（aircraft_reservation）。
    /// 単票編集（/E）と特別塗装ニュースの承認から予約し、反映日が来たら日次ジョブが
    /// <see cref="ApplyDueAsync"/> で反映する。反映は単票編集と同じ <see cref="AircraftStore"/> を通る。
    ///
    /// 予約の中身は「変える項目だけ」の差分にしている。機体まるごとを持つと、予約から反映までの
    /// あいだに別の項目（運用状況など）を直したときに、反映した日に古い値へ巻き戻ってしまうため。
    /// </summary>
    public static class AircraftReservationStore
    {
        /// <summary>画面にそのまま返せる結果</summary>
        public sealed class Result
        {
            public string? Message { get; set; }

            public string? Error { get; set; }
        }

        /// <summary>予約1件を反映した結果。Slack通知と画面に使う</summary>
        public sealed record ApplyOutcome(AircraftReservation Reservation, List<ReservedChange> Changes);

        /// <summary>
        /// 予約できる項目。レジは主キー、日時の3つはAircraftStoreが決めるので入れない。
        /// 並びは単票編集の画面の順。
        /// </summary>
        public static readonly IReadOnlyList<string> RESERVABLE_FIELDS =
        [
            nameof(Aircraft.Airline),
            nameof(Aircraft.TypeDetailId),
            nameof(Aircraft.SerialNumber),
            nameof(Aircraft.RegisterDate),
            nameof(Aircraft.OperationCode),
            nameof(Aircraft.WifiCode),
            nameof(Aircraft.SpecialLivery),
            nameof(Aircraft.Remarks),
            nameof(Aircraft.LinkUrl),
            nameof(Aircraft.SeatConfig),
            nameof(Aircraft.TestRegistration),
            nameof(Aircraft.MaintenanceNotify),
        ];

        private static readonly JsonSerializerOptions JSON_OPTIONS = new()
        {
            //日本語をエスケープせずに入れる（DBを直接見ても読めるように）
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// 予約中かどうか。日次ジョブと画面の「いま反映する」が重なって同じ予約を二度反映しないよう見張る。
        /// </summary>
        public static bool Processing { get; private set; }

        private static readonly object processingLock = new();

        // ======================================================================
        //  差分
        // ======================================================================

        /// <summary>いまの機体と編集後の機体を比べて、違う項目だけを返す</summary>
        public static List<ReservedChange> Diff(Aircraft current, Aircraft edited)
        {
            List<ReservedChange> changes = [];
            foreach (string field in RESERVABLE_FIELDS)
            {
                PropertyInfo prop = Property(field);
                object? before = prop.GetValue(current);
                object? after = prop.GetValue(edited);
                if (!SameValue(before, after))
                {
                    changes.Add(Change(field, before, Normalize(after)));
                }
            }
            return changes;
        }

        /// <summary>項目1つ分の変更を作る</summary>
        public static ReservedChange Change(string field, object? before, object? after) => new()
        {
            Field = field,
            Before = JsonSerializer.SerializeToElement(before),
            After = JsonSerializer.SerializeToElement(after),
        };

        public static string Serialize(IEnumerable<ReservedChange> changes)
            => JsonSerializer.Serialize(changes, JSON_OPTIONS);

        public static List<ReservedChange> Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }
            try
            {
                return JsonSerializer.Deserialize<List<ReservedChange>>(json) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        /// <summary>
        /// 予約した値を機体へ入れる。すでにその値になっている項目は数えない。
        /// 実際に変えた項目を返す（0件なら機体はまったく変わっていない）。
        /// </summary>
        public static List<ReservedChange> ApplyTo(Aircraft aircraft, IEnumerable<ReservedChange> changes)
        {
            List<ReservedChange> applied = [];
            foreach (ReservedChange change in changes)
            {
                if (!RESERVABLE_FIELDS.Contains(change.Field))
                {
                    //予約できない項目がDBに入っていても触らない
                    continue;
                }
                PropertyInfo prop = Property(change.Field);
                object? after = change.After.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                    ? null
                    : change.After.Deserialize(prop.PropertyType);
                if (after == null && prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) == null)
                {
                    //int（詳細型式）にnullは入れられない
                    continue;
                }

                object? current = prop.GetValue(aircraft);
                if (SameValue(current, after))
                {
                    continue;
                }
                prop.SetValue(aircraft, after);
                applied.Add(change);
            }
            return applied;
        }

        /// <summary>フォームから来る空文字とDBのnullは同じものとして扱う</summary>
        private static bool SameValue(object? a, object? b) => Equals(Normalize(a), Normalize(b));

        private static object? Normalize(object? value)
            => value is string s && string.IsNullOrWhiteSpace(s) ? null : value;

        private static PropertyInfo Property(string field)
            => typeof(Aircraft).GetProperty(field) ?? throw new ArgumentException($"Aircraftに{field}がありません。");

        /// <summary>画面に出す項目名（単票編集のラベルと同じ）</summary>
        public static string FieldName(string field)
        {
            PropertyInfo? prop = typeof(Aircraft).GetProperty(field);
            return prop?.GetCustomAttribute<DisplayAttribute>()?.Name ?? field;
        }

        /// <summary>画面に出す値。コードはマスタから名前を引く</summary>
        public static string ValueText(string field, JsonElement value)
        {
            if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                return "（なし）";
            }

            string raw = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
            return field switch
            {
                nameof(Aircraft.Airline) => MasterManager.AllAirline?.FirstOrDefault(a => a.AirlineCode == raw)?.AirlineNameJpShort ?? raw,
                nameof(Aircraft.OperationCode) => MasterManager.Operation?.FirstOrDefault(c => c.Key == raw)?.Value ?? raw,
                nameof(Aircraft.WifiCode) => MasterManager.Wifi?.FirstOrDefault(c => c.Key == raw)?.Value ?? raw,
                nameof(Aircraft.TypeDetailId) => int.TryParse(raw, out int td)
                    ? MasterManager.TypeDetailGroup?.FirstOrDefault(t => t.TypeDetailId == td)?.TypeDetailName ?? raw
                    : raw,
                nameof(Aircraft.SeatConfig) => int.TryParse(raw, out int sc)
                    ? MasterManager.SeatConfiguration?.FirstOrDefault(s => s.ConfigId == sc)?.SeatConfigLong ?? raw
                    : raw,
                nameof(Aircraft.MaintenanceNotify) => value.ValueKind == JsonValueKind.True ? "あり" : "なし",
                _ => raw,
            };
        }

        // ======================================================================
        //  予約
        // ======================================================================

        /// <summary>予約日として受け付けるか。当日分はもう日次ジョブが走ったあとかもしれないので明日以降だけ</summary>
        public static string? ValidateApplyDate(DateTime? applyDate, DateTime today)
        {
            if (applyDate == null)
            {
                return "予約日を入力してください。";
            }
            if (applyDate.Value.Date <= today.Date)
            {
                return "予約日は明日以降の日付にしてください（今日の分はそのまま登録してください）。";
            }
            return null;
        }

        /// <summary>
        /// 単票編集の内容を予約する。いまの機体と比べて違う項目だけを積む（SaveChangesは呼ぶ）。
        /// </summary>
        public static async Task<Result> ReserveFromEditAsync(
            JAFleetContext context, Aircraft edited, DateTime? applyDate, bool writeHistory, DateTime now)
        {
            string? invalid = ValidateApplyDate(applyDate, now);
            if (invalid != null)
            {
                return new Result { Error = invalid };
            }

            string reg = (edited.RegistrationNumber ?? "").Trim().ToUpperInvariant();
            Aircraft? current = await context.Aircrafts.AsNoTracking().FirstOrDefaultAsync(a => a.RegistrationNumber == reg);
            if (current == null)
            {
                return new Result { Error = $"{reg}はまだ登録されていないため予約できません。新規登録はそのまま登録してください。" };
            }

            List<ReservedChange> changes = Diff(current, edited);
            if (changes.Count == 0)
            {
                return new Result { Error = "いまの内容から変わっている項目がないため、予約しませんでした。" };
            }

            Add(context, reg, applyDate!.Value, changes, writeHistory, "単票編集", now);
            await context.SaveChangesAsync();

            return new Result
            {
                Message = $"{applyDate:yyyy/MM/dd}に反映するよう予約しました（{string.Join("・", changes.Select(c => FieldName(c.Field)))}）。"
                    + "いまの機体情報は変えていません。",
            };
        }

        /// <summary>
        /// 予約を1件積む（SaveChangesは呼ばない）。特別塗装ニュースの承認からも使う。
        /// </summary>
        public static AircraftReservation Add(
            JAFleetContext context, string registration, DateTime applyDate,
            IEnumerable<ReservedChange> changes, bool writeHistory, string? source, DateTime now)
        {
            AircraftReservation reservation = new()
            {
                RegistrationNumber = registration,
                ApplyDate = applyDate.Date,
                Changes = Serialize(changes),
                WriteHistory = writeHistory,
                Source = source is { Length: > 512 } ? source[..512] : source,
                Status = AircraftReservationStatus.PENDING,
                CreatedAt = now,
            };
            context.AircraftReservations.Add(reservation);
            return reservation;
        }

        /// <summary>予約を取り消す。aircraftは変えない</summary>
        public static async Task<Result> CancelAsync(JAFleetContext context, int reservationId)
        {
            AircraftReservation? reservation = await context.AircraftReservations
                .FirstOrDefaultAsync(r => r.ReservationId == reservationId);
            if (reservation == null)
            {
                return new Result { Error = "予約が見つかりません。" };
            }
            if (reservation.Status != AircraftReservationStatus.PENDING)
            {
                return new Result { Error = $"この予約はすでに{AircraftReservationStatus.GetName(reservation.Status)}です。" };
            }

            reservation.Status = AircraftReservationStatus.CANCELED;
            reservation.ResolvedAt = DateTime.Now;
            await context.SaveChangesAsync();
            return new Result
            {
                Message = $"{reservation.RegistrationNumber}の{reservation.ApplyDate:yyyy/MM/dd}の予約を取り消しました。機体情報は変えていません。",
            };
        }

        // ======================================================================
        //  反映
        // ======================================================================

        /// <summary>
        /// 反映日が今日以前の予約をまとめて反映する。日次ジョブと画面の「いま反映する」から呼ぶ。
        /// 前日以前の分も拾うのは、ジョブが止まっていた日の予約を取りこぼさないため。
        /// 1件ずつ保存するので、1件が失敗しても残りは反映される。すでに実行中ならnullを返す。
        /// </summary>
        public static async Task<List<ApplyOutcome>?> ApplyDueAsync(JAFleetContext context, DateTime now)
        {
            lock (processingLock)
            {
                if (Processing)
                {
                    return null;
                }
                Processing = true;
            }

            try
            {
                DateTime today = now.Date;
                List<int> ids = await context.AircraftReservations.AsNoTracking()
                    .Where(r => r.Status == AircraftReservationStatus.PENDING && r.ApplyDate <= today)
                    //同じ機体に複数あれば、反映日の古い順・予約した順に重ねる
                    .OrderBy(r => r.ApplyDate).ThenBy(r => r.ReservationId)
                    .Select(r => r.ReservationId)
                    .ToListAsync();

                List<ApplyOutcome> outcomes = [];
                foreach (int id in ids)
                {
                    outcomes.Add(await ApplyOneAsync(context, id, now));
                }
                return outcomes;
            }
            finally
            {
                Processing = false;
            }
        }

        private static async Task<ApplyOutcome> ApplyOneAsync(JAFleetContext context, int reservationId, DateTime now)
        {
            AircraftReservation reservation = await context.AircraftReservations.FirstAsync(r => r.ReservationId == reservationId);
            List<ReservedChange> changes = Deserialize(reservation.Changes);
            try
            {
                Aircraft? aircraft = await context.Aircrafts.FirstOrDefaultAsync(a => a.RegistrationNumber == reservation.RegistrationNumber);
                if (aircraft == null)
                {
                    reservation.Status = AircraftReservationStatus.FAILED;
                    reservation.ResultMessage = "機体が見つかりません（レジが変わったか削除された）。";
                }
                else
                {
                    List<ReservedChange> applied = ApplyTo(aircraft, changes);
                    if (applied.Count == 0)
                    {
                        reservation.Status = AircraftReservationStatus.NO_DIFF;
                        reservation.ResultMessage = "反映日の時点で、すべての項目がすでに予約した値になっていました。";
                    }
                    else
                    {
                        AircraftStore.Store(context, aircraft, isNew: false, reservation.WriteHistory, now);
                        reservation.Status = AircraftReservationStatus.APPLIED;
                        List<string> skipped = [.. changes.Where(c => !applied.Contains(c)).Select(c => FieldName(c.Field))];
                        reservation.ResultMessage = skipped.Count == 0
                            ? null
                            : $"すでに予約した値になっていたため触らなかった項目: {string.Join("・", skipped)}";
                    }
                }
                reservation.ResolvedAt = now;
                await context.SaveChangesAsync();
                return new ApplyOutcome(reservation, changes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AircraftReservation] 反映に失敗しました（{reservation.RegistrationNumber} #{reservationId}）: {ex.Message}");
                //積みかけた変更は捨て、失敗したことだけ記録する
                foreach (var entry in context.ChangeTracker.Entries().ToList())
                {
                    entry.State = EntityState.Detached;
                }
                AircraftReservation failed = await context.AircraftReservations.FirstAsync(r => r.ReservationId == reservationId);
                failed.Status = AircraftReservationStatus.FAILED;
                failed.ResultMessage = ex.InnerException?.Message ?? ex.Message;
                failed.ResolvedAt = now;
                await context.SaveChangesAsync();
                return new ApplyOutcome(failed, changes);
            }
        }

        /// <summary>反映結果をSlackへ出す文面。1件も無ければnull</summary>
        public static string? BuildSlackText(IReadOnlyCollection<ApplyOutcome> outcomes, string siteUrl)
        {
            if (outcomes.Count == 0)
            {
                return null;
            }

            IEnumerable<string> lines = outcomes.Select(o =>
            {
                string mark = o.Reservation.Status switch
                {
                    AircraftReservationStatus.APPLIED => "✅",
                    AircraftReservationStatus.NO_DIFF => "➖",
                    _ => "⚠️",
                };
                string detail = string.Join("、", o.Changes.Select(c => $"{FieldName(c.Field)}→{ValueText(c.Field, c.After)}"));
                string status = AircraftReservationStatus.GetName(o.Reservation.Status);
                string result = string.IsNullOrEmpty(o.Reservation.ResultMessage) ? "" : $"（{o.Reservation.ResultMessage}）";
                return $"{mark} <{siteUrl}/AD/{o.Reservation.RegistrationNumber}|{o.Reservation.RegistrationNumber}> {status}: {detail}{result}";
            });

            return $":calendar: *機体更新の予約を反映しました（{outcomes.Count}件）*\n"
                + string.Join("\n", lines) + "\n"
                + $"<{siteUrl}/AircraftReservation?fromAdmin=true|予約の一覧を開く>";
        }
    }
}
