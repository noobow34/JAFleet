using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Services.Livery
{
    /// <summary>
    /// 特別塗装の更新候補を承認・却下する。承認したときだけaircraftへ反映する。
    /// 反映は単票編集（/E）と同じAircraftStoreを通すので、更新前の内容はaircraft_historyに残る。
    /// </summary>
    public static class LiveryNewsStore
    {
        /// <summary>画面にそのまま返せる結果</summary>
        public sealed class Result
        {
            public string? Message { get; set; }

            public string? Error { get; set; }

            /// <summary>エラーのときに、どの行で失敗したのかを画面で示すため</summary>
            public int? ErrorCandidateId { get; set; }
        }

        /// <summary>
        /// 候補を承認してaircraft.special_liveryへ入れる。
        /// レジと塗装名は画面で直せるので、Geminiが出した値ではなく渡された値を使う。
        /// </summary>
        public static async Task<Result> ApproveAsync(
            JAFleetContext context, int candidateId, string? registration, string? liveryName)
        {
            LiveryNewsCandidate? candidate = await context.LiveryNewsCandidates
                .FirstOrDefaultAsync(c => c.CandidateId == candidateId);
            if (candidate == null)
            {
                return new Result { Error = "候補が見つかりません。" };
            }
            if (candidate.Status != LiveryNewsStatus.PENDING)
            {
                return new Result
                {
                    Error = $"この候補はすでに{LiveryNewsStatus.GetName(candidate.Status)}済みです。",
                    ErrorCandidateId = candidateId,
                };
            }

            string? reg = LiveryNewsChecker.NormalizeRegistration(registration);
            if (reg == null)
            {
                return new Result { Error = "レジを「JA」から始まる形で入力してください。", ErrorCandidateId = candidateId };
            }

            string livery = (liveryName ?? "").Trim();
            if (livery.Length == 0)
            {
                return new Result { Error = "塗装名を入力してください。", ErrorCandidateId = candidateId };
            }
            if (livery.Length > LiveryNewsConstant.MAX_LIVERY_LENGTH)
            {
                return new Result
                {
                    Error = $"塗装名は{LiveryNewsConstant.MAX_LIVERY_LENGTH}文字以内で入力してください。",
                    ErrorCandidateId = candidateId,
                };
            }

            Aircraft? aircraft = await context.Aircrafts.FirstOrDefaultAsync(a => a.RegistrationNumber == reg);
            if (aircraft == null)
            {
                return new Result { Error = $"{reg}はJA-Fleetに登録されていません。", ErrorCandidateId = candidateId };
            }

            DateTime now = DateTime.Now;
            aircraft.SpecialLivery = livery;
            //更新前の内容をaircraft_historyへ退避する（機体詳細の更新履歴に出る）
            AircraftStore.Store(context, aircraft, isNew: false, writeHistory: true, now);

            //Geminiが出した値はlivery_news.gemini_resultに残るので、ここは実際に反映した値で上書きする
            candidate.RegistrationNumber = reg;
            candidate.AppliedLivery = livery;
            candidate.Status = LiveryNewsStatus.APPROVED;
            candidate.ResolvedAt = now;

            await context.SaveChangesAsync();

            return new Result { Message = $"{reg}の特別塗装を「{livery}」に更新しました。" };
        }

        /// <summary>候補を却下する。aircraftは何も変えない</summary>
        public static async Task<Result> RejectAsync(JAFleetContext context, int candidateId)
        {
            LiveryNewsCandidate? candidate = await context.LiveryNewsCandidates
                .FirstOrDefaultAsync(c => c.CandidateId == candidateId);
            if (candidate == null)
            {
                return new Result { Error = "候補が見つかりません。" };
            }
            if (candidate.Status != LiveryNewsStatus.PENDING)
            {
                return new Result
                {
                    Error = $"この候補はすでに{LiveryNewsStatus.GetName(candidate.Status)}済みです。",
                    ErrorCandidateId = candidateId,
                };
            }

            candidate.Status = LiveryNewsStatus.REJECTED;
            candidate.ResolvedAt = DateTime.Now;
            await context.SaveChangesAsync();

            return new Result { Message = "候補を却下しました。機体情報は変えていません。" };
        }
    }
}
