using System.Text.RegularExpressions;
using JAFleet.Commons.Data;
using JAFleet.Commons.Util;
using Noobow.Commons.Utils;
using JAFleet.Models;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Services
{
    /// <summary>
    /// 検索画面（/Search）の条件で aircraft_view を絞り込む。棚卸（/Inventory）も同じ条件で探すので、ここにまとめてある。
    /// </summary>
    public static class AircraftSearch
    {
        /// <summary>不正な正規表現を渡されたときに画面へ出す文言</summary>
        public const string INVALID_REGEX_MESSAGE = "不正な正規表現が指定されました。正規表現を修正してください。正しい正規表現にもかかわらずこのメッセージが表示される場合は管理人にご連絡ください。";

        /// <summary>
        /// 条件どおりに絞り込んだ問い合わせを作る（並びは DisplayOrder）。まだ DB には問い合わせない。
        /// 特別塗装の「履歴含む」では、履歴にだけ特別塗装がある機体のレジを <paramref name="historyLiveryRegs"/> に返す
        /// （結果を出すときに <see cref="MarkHistoryLivery"/> で「（履歴あり）」を添える）。
        /// </summary>
        public static IQueryable<AircraftView> Build(JAFleetContext context, SearchModel model, out List<string> historyLiveryRegs)
        {
            var regList = new List<string?>();
            historyLiveryRegs = [];

            if (model.RegistrationNumber != null)
            {
                //|区切りで複数件を処理。JAがついていなければ付加
                foreach (string r in model.RegistrationNumber.ToUpper().Split("|"))
                {
                    regList.Add(r.StartsWith("JA") ? r : "JA" + r);
                }
            }

            IQueryable<AircraftView> query = context.AircraftViews.AsNoTracking();
            if (!string.IsNullOrEmpty(model.Airline))
            {
                string[] airline = model.Airline.Split("|");
                query = query.Where(p => airline.Contains(p.Airline));
            }

            if (!string.IsNullOrEmpty(model.TypeDetail))
            {
                int[] typeDetail = model.TypeDetail.Split("|").Select(t => Convert.ToInt32(t)).ToArray();
                query = query.Where(p => typeDetail.Contains(p.TypeDetailId));
            }

            if (!string.IsNullOrEmpty(model.WiFiCode))
            {
                string[] wifi = model.WiFiCode.Split("|");
                query = query.Where(p => wifi.Contains(p.WifiCode));
            }

            //登録年月・抹消年月（yyyy/MM）で絞る。「と等しい」はその月、「以前」「以降」はその月を含む
            if (MonthRange(model.RegistrationDate) is (DateOnly regFirst, DateOnly regLast))
            {
                query = model.RegistrationSelection switch
                {
                    "1" => query.Where(p => p.RegisterDate <= regLast),
                    "2" => query.Where(p => p.RegisterDate >= regFirst),
                    _ => query.Where(p => p.RegisterDate >= regFirst && p.RegisterDate <= regLast),
                };
            }
            if (MonthRange(model.DeregistrationDate) is (DateOnly deregFirst, DateOnly deregLast))
            {
                query = model.DeregistrationSelection switch
                {
                    "1" => query.Where(p => p.DeregisterDate <= deregLast),
                    "2" => query.Where(p => p.DeregisterDate >= deregFirst),
                    _ => query.Where(p => p.DeregisterDate >= deregFirst && p.DeregisterDate <= deregLast),
                };
            }

            if (!string.IsNullOrEmpty(model.OperationCode))
            {
                string[] operation = model.OperationCode.Split("|");
                query = query.Where(p => operation.Contains(p.OperationCode));
            }

            if (model.Remarks == "1")
            {
                query = query.Where(p => string.IsNullOrEmpty(p.Remarks));
            }
            else if (model.Remarks == "2")
            {
                query = query.Where(p => !string.IsNullOrEmpty(p.Remarks));
            }
            else if (model.Remarks == "3")
            {
                query = query.Where(p => p.Remarks!.Contains(model.RemarksKeyword));
            }

            if (model.SpecialLivery == "1")
            {
                query = query.Where(p => string.IsNullOrEmpty(p.SpecialLivery));
            }
            else if (model.SpecialLivery == "2")
            {
                query = query.Where(p => !string.IsNullOrEmpty(p.SpecialLivery));
            }
            else if (model.SpecialLivery == "3")
            {
                query = query.Where(p => p.SpecialLivery!.Contains(model.SpecialLiveryKeyword));
            }
            else if (model.SpecialLivery == "4")
            {
                //あり（履歴含む）
                //一旦履歴テーブルを検索して、履歴の中の該当のレジを取得
                historyLiveryRegs.AddRange(context.AircraftHistories.Where(p => !string.IsNullOrEmpty(p.SpecialLivery)).Select(s => s.RegistrationNumber).Distinct().ToList());
                regList.AddRange(historyLiveryRegs);
                regList.AddRange(context.Aircrafts.Where(p => !string.IsNullOrEmpty(p.SpecialLivery)).Select(s => s.RegistrationNumber).Distinct().ToList());
            }
            else if (model.SpecialLivery == "5")
            {
                //キーワード指定（履歴含む）
                historyLiveryRegs.AddRange(context.AircraftHistories.Where(p => p.SpecialLivery!.Contains(model.SpecialLiveryKeyword)).Select(s => s.RegistrationNumber).Distinct().ToList());
                regList.AddRange(historyLiveryRegs);
                regList.AddRange(context.Aircrafts.Where(p => p.SpecialLivery!.Contains(model.SpecialLiveryKeyword)).Select(s => s.RegistrationNumber).Distinct().ToList());
            }

            if (regList.Count == 1)
            {
                string reg = regList[0]!;
                //メタ文字を含む場合正規表現検索、含まない場合＝検索
                query = StringUtil.ContainsMetaCharacter(reg)
                    ? query.Where(a => Regex.IsMatch(a.RegistrationNumber, reg))
                    : query.Where(a => a.RegistrationNumber == reg);
            }
            else if (regList.Count > 1)
            {
                //2件以上の場合は正規表現無効でIN検索
                query = query.Where(p => regList.Contains(p.RegistrationNumber));
            }

            return query.OrderBy(p => p.DisplayOrder);
        }

        /// <summary>DB が正規表現を受け付けなかった例外か</summary>
        public static bool IsInvalidRegex(Npgsql.PostgresException ex) => ex.Message.Contains("invalid regular expression");

        /// <summary>履歴から見つけた機体の特別塗装に「（履歴あり）」を添える</summary>
        public static void MarkHistoryLivery(IEnumerable<AircraftView> result, List<string> historyLiveryRegs)
        {
            var regs = historyLiveryRegs.ToHashSet();
            foreach (AircraftView a in result.Where(a => regs.Contains(a.RegistrationNumber)))
            {
                a.SpecialLivery += "（履歴あり）";
            }
        }

        /// <summary>"yyyy/MM" をその月の初日と末日にする。空・読めないときは null（その条件では絞らない）</summary>
        private static (DateOnly First, DateOnly Last)? MonthRange(string? month)
        {
            if (string.IsNullOrEmpty(month) || !JaDateUtil.TryParse(month, out DateOnly? monthEnd) || monthEnd == null)
            {
                return null;
            }
            DateOnly last = monthEnd.Value;
            return (new DateOnly(last.Year, last.Month, 1), last);
        }
    }
}
