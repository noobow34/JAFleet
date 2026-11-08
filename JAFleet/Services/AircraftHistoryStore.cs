using JAFleet.Commons.Data;
using JAFleet.Models;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Services
{
    /// <summary>
    /// 更新履歴（aircraft_history）の修正と削除。
    /// 履歴は単票編集や取込のときにAircraftStoreが自動で積むもので、普段は触らない。
    /// 間違った内容のまま履歴が残ってしまったときに直すためのもの。
    /// </summary>
    public static class AircraftHistoryStore
    {
        /// <summary>画面にそのまま返せる結果</summary>
        public sealed class Result
        {
            /// <summary>処理した履歴のレジ</summary>
            public string? Reg { get; set; }

            public string? Message { get; set; }

            public string? Error { get; set; }

            /// <summary>エラーがどの行のものか。画面でその行に目印を出す。</summary>
            public int? ErrorAhId { get; set; }
        }

        /// <summary>
        /// DBを見ないで分かる範囲の、1行分の検査。
        /// 詳細型式は必須。aircraft_history_viewがtype_detailと突き合わせているため、
        /// 未設定のままだと詳細画面の履歴からその行が消えてしまう。
        /// </summary>
        public static string? Validate(AircraftHistoryInput input)
        {
            if (input.Seq is null or <= 0)
            {
                return "SEQは1以上で入力してください。";
            }
            if (string.IsNullOrEmpty(input.Airline?.Trim()))
            {
                return "航空会社を選択してください。";
            }
            if (input.TypeDetailId <= 0)
            {
                return "詳細型式を選択してください。";
            }
            if (input.HistoryRegisterAt == null)
            {
                return "履歴登録日時を入力してください。";
            }
            return null;
        }

        /// <summary>
        /// まとめて保存する分の検査。1行ずつの検査に加えて、SEQが送られてきた中で
        /// 重なっていないかを見る（画面で入れ替えるとDBと突き合わせるだけでは拾えない）。
        /// </summary>
        public static Result? ValidateAll(IList<AircraftHistoryInput> inputs)
        {
            foreach (AircraftHistoryInput input in inputs)
            {
                string? invalid = Validate(input);
                if (invalid != null)
                {
                    return new Result { Error = invalid, ErrorAhId = input.AhId };
                }
            }

            AircraftHistoryInput? duplicated = inputs
                .GroupBy(i => i.Seq)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g)
                .FirstOrDefault();
            if (duplicated != null)
            {
                return new Result
                {
                    Error = $"SEQ {duplicated.Seq} が複数の履歴で重なっています。",
                    ErrorAhId = duplicated.AhId,
                };
            }

            return null;
        }

        /// <summary>
        /// 1レジ分の履歴をまとめて更新する。
        /// 1件でも通らなければ何も保存しない（画面の見た目とDBがずれないようにするため）。
        /// </summary>
        public static Result SaveAll(JAFleetContext context, string reg, IList<AircraftHistoryInput> inputs)
        {
            if (inputs.Count == 0)
            {
                return new Result { Reg = reg, Message = "保存する履歴がありません。" };
            }

            Result? invalid = ValidateAll(inputs);
            if (invalid != null)
            {
                invalid.Reg = reg;
                return invalid;
            }

            List<AircraftHistory> targets = [.. context.AircraftHistories
                .Where(ah => ah.RegistrationNumber == reg)];
            Dictionary<int, AircraftHistory> byId = targets
                .Where(ah => ah.AhId != null)
                .ToDictionary(ah => ah.AhId!.Value);

            HashSet<string> airlines = [.. context.Airlines.AsNoTracking().Select(a => a.AirlineCode)];
            HashSet<int> typeDetails = [.. context.TypeDetails.AsNoTracking()
                .Where(t => t.TypeDetailId != null).Select(t => t.TypeDetailId!.Value)];

            foreach (AircraftHistoryInput input in inputs)
            {
                if (!byId.ContainsKey(input.AhId))
                {
                    return new Result
                    {
                        Reg = reg,
                        Error = "表示していた履歴が見つかりませんでした。画面を開き直してください。",
                        ErrorAhId = input.AhId,
                    };
                }
                if (!airlines.Contains(input.Airline!.Trim()))
                {
                    return new Result
                    {
                        Reg = reg,
                        Error = $"航空会社 {input.Airline!.Trim()} がマスタにありません。",
                        ErrorAhId = input.AhId,
                    };
                }
                if (!typeDetails.Contains(input.TypeDetailId))
                {
                    return new Result
                    {
                        Reg = reg,
                        Error = "選んだ詳細型式がマスタにありません。",
                        ErrorAhId = input.AhId,
                    };
                }
            }

            foreach (AircraftHistoryInput input in inputs)
            {
                Apply(byId[input.AhId], input);
            }

            //実際に変わった行だけ数える。何も変えずに保存したときに「更新しました」と出さないため。
            int changed = context.ChangeTracker.Entries<AircraftHistory>()
                .Count(e => e.State == EntityState.Modified);
            context.SaveChanges();

            return new Result
            {
                Reg = reg,
                Message = changed == 0 ? "変更はありませんでした。" : $"{changed}件の履歴を更新しました。",
            };
        }

        /// <summary>
        /// いまの機体情報をそのままコピーして履歴を1件作る。リカバリ専用。
        /// 単票編集で「履歴を作成しない」のチェックを外し忘れると、更新前の内容がどこにも残らずに
        /// 消えてしまう。そのとき、器だけ作って中身と日時を直せるようにするためのもの。
        /// 普段の履歴は機体を更新した結果として積まれるので、ここは通常の入口ではない。
        /// </summary>
        public static Result Snapshot(JAFleetContext context, string reg, DateTime at)
        {
            AircraftHistory? created = AircraftStore.AddHistory(context, reg, at);
            if (created == null)
            {
                return new Result { Reg = reg, Error = $"{reg} は機体マスタにありません。履歴を作れません。" };
            }

            context.SaveChanges();

            return new Result
            {
                Reg = reg,
                Message = $"いまの内容から履歴を1件作りました（SEQ {created.Seq}）。中身と日時を直してください。",
            };
        }

        /// <summary>1件分の履歴を削除する。SEQは詰めない（並び順だけに使うので飛んでいても困らない）。</summary>
        public static Result Delete(JAFleetContext context, int ahId)
        {
            AircraftHistory? target = context.AircraftHistories
                .FirstOrDefault(ah => ah.AhId == ahId);
            if (target == null)
            {
                return new Result { Error = "対象の履歴が見つかりませんでした。" };
            }

            string reg = target.RegistrationNumber;
            int? seq = target.Seq;
            context.AircraftHistories.Remove(target);
            context.SaveChanges();

            return new Result { Reg = reg, Message = $"{reg} のSEQ {seq} の履歴を削除しました。" };
        }

        private static void Apply(AircraftHistory target, AircraftHistoryInput input)
        {
            target.Seq = input.Seq;
            target.HistoryRegisterAt = Keep(target.HistoryRegisterAt, input.HistoryRegisterAt);
            target.Airline = input.Airline!.Trim();
            target.TypeDetailId = input.TypeDetailId;
            target.SerialNumber = Empty(input.SerialNumber);
            target.RegisterDate = Empty(input.RegisterDate);
            target.OperationCode = Empty(input.OperationCode);
            target.WifiCode = Empty(input.WifiCode);
            target.SeatConfig = input.SeatConfig;
            target.SpecialLivery = Empty(input.SpecialLivery);
            target.Remarks = Empty(input.Remarks);
            target.LinkUrl = Empty(input.LinkUrl);
            target.TestRegistration = Empty(input.TestRegistration);
            //未設定（NULL）のものをチェックしないまま保存したときに、falseで埋めない
            if (!(target.MaintenanceNotify == null && !input.MaintenanceNotify))
            {
                target.MaintenanceNotify = input.MaintenanceNotify;
            }
            target.CreationTime = Keep(target.CreationTime, input.CreationTime);
            target.UpdateTime = Keep(target.UpdateTime, input.UpdateTime);
            target.ActualUpdateTime = Keep(target.ActualUpdateTime, input.ActualUpdateTime);
        }

        /// <summary>
        /// 画面は日時を秒までしか出さないので、そのまま受け取ると秒未満が毎回切り捨てられ、
        /// 触っていない行まで更新されてしまう。秒まで一致していれば元の値を残す。
        /// </summary>
        private static DateTime? Keep(DateTime? stored, DateTime? input)
        {
            if (stored == null || input == null)
            {
                return input;
            }

            DateTime truncated = new(stored.Value.Year, stored.Value.Month, stored.Value.Day,
                                     stored.Value.Hour, stored.Value.Minute, stored.Value.Second,
                                     stored.Value.Kind);
            return truncated == input.Value ? stored : input;
        }

        private static string? Empty(string? value)
            => string.IsNullOrEmpty(value?.Trim()) ? null : value!.Trim();
    }
}
