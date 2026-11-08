using JAFleet.Commons.Data;
using JAFleet.Commons.Util;
using JAFleet.Models;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Services
{
    /// <summary>
    /// コンフィグ（seat_configuration）の登録・更新・削除。
    /// 単票編集（/E）と同一型式の一括登録はここで登録された行を選ぶだけなので、
    /// 行を足すにはこれまでDBを直接触る必要があった。その入口がコンフィグマスタ（/SeatConfig）。
    /// </summary>
    public static class SeatConfigStore
    {
        /// <summary>1区分あたりの席数の上限。打ち間違いを弾くためだけの値。</summary>
        public const int MAX_SEAT = 1000;

        public const int MAX_URL_LENGTH = 500;

        /// <summary>画面にそのまま返せる結果</summary>
        public sealed class Result
        {
            public int? Id { get; set; }

            /// <summary>その場登録で選択肢に出す表示名（「C12,Y134 (146) (ANA-B738)」）</summary>
            public string? Name { get; set; }

            /// <summary>同じ内容が既にあったので作らずにそれを返した（その場登録のみ）</summary>
            public bool Duplicated { get; set; }

            public string? Message { get; set; }

            public string? Error { get; set; }
        }

        /// <summary>
        /// DBを見ないで分かる範囲の検査。
        /// 席数は空欄が「その区分なし」なので、0を入れると「Y0」のような表示になってしまう。
        /// そのため0以下は通さない。
        /// </summary>
        public static string? Validate(SeatConfigInput input)
        {
            if (string.IsNullOrEmpty(input.Airline?.Trim()))
            {
                return "航空会社を選択してください。";
            }
            if (string.IsNullOrEmpty(input.Type?.Trim()))
            {
                return "型式を選択してください。";
            }

            (string Name, int? Value)[] seats =
            [
                ("F", input.F), ("C", input.C), ("PY", input.PY),
                ("P", input.P), ("J", input.J), ("Y", input.Y),
            ];

            if (seats.All(s => !s.Value.HasValue))
            {
                return "席数を1つ以上入力してください。";
            }
            foreach ((string name, int? value) in seats)
            {
                if (value is <= 0)
                {
                    return $"{name}の席数は1以上で入力してください。その区分が無い場合は空欄にしてください。";
                }
                if (value > MAX_SEAT)
                {
                    return $"{name}の席数が大きすぎます。{MAX_SEAT}席までです。";
                }
            }

            string url = input.Url?.Trim() ?? string.Empty;
            if (url.Length > MAX_URL_LENGTH)
            {
                return $"URLが長すぎます。{MAX_URL_LENGTH}文字までです。";
            }
            if (url.Length > 0 && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                               && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "URLはhttp://またはhttps://で始めてください。";
            }

            return null;
        }

        /// <summary>ConfigIdがあれば更新、無ければ新規登録する。</summary>
        public static Result Save(JAFleetContext context, SeatConfigInput input)
        {
            string? invalid = Validate(input);
            if (invalid != null)
            {
                return new Result { Error = invalid };
            }

            string airline = input.Airline!.Trim();
            string type = input.Type!.Trim();
            string? url = string.IsNullOrEmpty(input.Url?.Trim()) ? null : input.Url!.Trim();

            string? unknown = CheckMaster(context, airline, type);
            if (unknown != null)
            {
                return new Result { Error = unknown };
            }

            SeatConfiguration? duplicated = FindDuplicate(context, input, airline, type);
            if (duplicated != null)
            {
                return new Result
                {
                    Error = $"同じ内容のコンフィグが既にあります。（{airline}-{type} {duplicated.SeatConfig}）",
                };
            }

            SeatConfiguration target;
            bool isNew = input.ConfigId == null;
            if (isNew)
            {
                target = new SeatConfiguration();
                context.SeatConfigurations.Add(target);
            }
            else
            {
                SeatConfiguration? found = context.SeatConfigurations
                    .FirstOrDefault(sc => sc.ConfigId == input.ConfigId);
                if (found == null)
                {
                    return new Result { Error = "対象のコンフィグが見つかりませんでした。" };
                }
                target = found;
            }

            target.Airline = airline;
            target.Type = type;
            target.Url = url;
            target.F = input.F;
            target.C = input.C;
            target.PY = input.PY;
            target.P = input.P;
            target.J = input.J;
            target.Y = input.Y;

            context.SaveChanges();
            ReloadMaster(context);

            string seatConfig = SeatConfigUtil.GenerateSeatConfigString(input.F, input.C, input.PY, input.P, input.J, input.Y);
            return new Result
            {
                Id = target.ConfigId,
                Message = $"{airline}-{type} {seatConfig} を{(isNew ? "登録" : "更新")}しました。",
            };
        }

        /// <summary>
        /// 編集中の画面を離れずに1件足すためのもの。単票編集（/E）のコンフィグ欄から呼ぶ。
        /// マスタ画面と違って、同じ内容が既にあってもエラーにせずそれを返す
        /// （欲しいのは「この座席仕様の行」であって、作ること自体ではないため）。
        /// </summary>
        public static Result Create(JAFleetContext context, SeatConfigInput input)
        {
            string? invalid = Validate(input);
            if (invalid != null)
            {
                return new Result { Error = invalid };
            }

            string airline = input.Airline!.Trim();
            string type = input.Type!.Trim();

            string? unknown = CheckMaster(context, airline, type);
            if (unknown != null)
            {
                return new Result { Error = unknown };
            }

            SeatConfiguration? duplicated = FindDuplicate(context, input, airline, type);
            if (duplicated != null)
            {
                return new Result
                {
                    Id = duplicated.ConfigId,
                    Name = duplicated.SeatConfigLong,
                    Duplicated = true,
                };
            }

            SeatConfiguration created = new()
            {
                Airline = airline,
                Type = type,
                Url = string.IsNullOrEmpty(input.Url?.Trim()) ? null : input.Url!.Trim(),
                F = input.F,
                C = input.C,
                PY = input.PY,
                P = input.P,
                J = input.J,
                Y = input.Y,
            };
            context.SeatConfigurations.Add(created);
            context.SaveChanges();
            ReloadMaster(context);

            return new Result { Id = created.ConfigId, Name = created.SeatConfigLong };
        }

        /// <summary>使っている機体が1件も無いコンフィグだけ削除する。</summary>
        public static Result Delete(JAFleetContext context, int configId)
        {
            SeatConfiguration? target = context.SeatConfigurations
                .FirstOrDefault(sc => sc.ConfigId == configId);
            if (target == null)
            {
                return new Result { Error = "対象のコンフィグが見つかりませんでした。" };
            }

            int used = context.Aircrafts.AsNoTracking().Count(a => a.SeatConfig == configId);
            if (used > 0)
            {
                return new Result
                {
                    Error = $"このコンフィグは{used}機が使っています。先に機体側の割り当てを変えてください。",
                };
            }

            string label = $"{target.Airline}-{target.Type} {target.SeatConfig}";
            context.SeatConfigurations.Remove(target);
            context.SaveChanges();
            ReloadMaster(context);

            return new Result { Message = $"{label} を削除しました。" };
        }

        /// <summary>航空会社・型式がマスタにあるか。無ければそのまま画面に出せる文を返す。</summary>
        private static string? CheckMaster(JAFleetContext context, string airline, string type)
        {
            if (!context.Airlines.AsNoTracking().Any(a => a.AirlineCode == airline))
            {
                return $"航空会社 {airline} がマスタにありません。";
            }
            if (!context.Types.AsNoTracking().Any(t => t.TypeCode == type))
            {
                return $"型式 {type} がマスタにありません。";
            }
            return null;
        }

        /// <summary>
        /// 航空会社・型式・席数がすべて同じ行。選ぶときに区別できないので、作る前に必ず見る。
        /// config_idは1から振られるので、新規のときは0と比べて全行を対象にする。
        /// </summary>
        private static SeatConfiguration? FindDuplicate(JAFleetContext context, SeatConfigInput input, string airline, string type)
        {
            int excludeId = input.ConfigId ?? 0;
            return context.SeatConfigurations.AsNoTracking()
                .FirstOrDefault(sc => sc.ConfigId != excludeId
                                   && sc.Airline == airline && sc.Type == type
                                   && sc.F == input.F && sc.C == input.C && sc.PY == input.PY
                                   && sc.P == input.P && sc.J == input.J && sc.Y == input.Y);
        }

        private static void ReloadMaster(JAFleetContext context)
        {
            try
            {
                MasterManager.ReadAll(context);
            }
            catch (Exception)
            {
                //キャッシュの更新に失敗しても登録自体は成功しているので、画面はそのまま進める
            }
        }
    }
}
