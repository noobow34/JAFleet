using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Services.Livery
{
    /// <summary>
    /// 判定を試すための入口。特別塗装のニュースが実際に出るのを待たなくても
    /// 「判定 → 候補 → Slack通知 → 承認 → 反映」の一連を通せるようにする。
    /// 本番の処理（<see cref="LiveryNewsChecker.SaveJudgementAsync"/>）をそのまま使うので、
    /// ここで動いたなら日次ジョブでも同じように動く。
    /// </summary>
    public static class LiveryNewsTrial
    {
        /// <summary>手動で作った候補に入れる記事URLの頭。httpでないので画面ではリンクにしない</summary>
        public const string MANUAL_URL_PREFIX = "manual:";

        /// <summary>画面にそのまま返せる結果</summary>
        public sealed class Result
        {
            /// <summary>Geminiの判定（お試し判定のときだけ入る）</summary>
            public GeminiLiveryResponse? Response { get; set; }

            /// <summary>Geminiが返したJSONそのまま。画面に出して判定の中身を確かめる</summary>
            public string? Raw { get; set; }

            /// <summary>記事と候補をDBに保存したか</summary>
            public bool Saved { get; set; }

            /// <summary>作った未処理の候補の件数</summary>
            public int PendingCount { get; set; }

            public string? Message { get; set; }

            public string? Error { get; set; }
        }

        /// <summary>
        /// 記事1本を判定する。saveがfalseなら判定結果を見るだけでDBには何も書かない。
        /// trueなら本番と同じく記事と候補を保存し、Slackへも通知する。
        /// </summary>
        public static async Task<Result> JudgeAsync(
            JAFleetContext context, string? url, string? title, string? summary, bool save)
        {
            string apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
            if (string.IsNullOrEmpty(apiKey))
            {
                return new Result { Error = "GEMINI_API_KEYが設定されていないため判定できません。" };
            }

            string articleUrl = (url ?? "").Trim();
            string? articleTitle = LiveryNewsChecker.Trim(title, 512);
            string? articleSummary = LiveryNewsChecker.Trim(summary, 4000);

            string? invalid = Validate(articleUrl, articleSummary, save);
            if (invalid != null)
            {
                return new Result { Error = invalid };
            }
            if (save && await context.LiveryNewsList.AnyAsync(n => n.NewsUrl == articleUrl))
            {
                return new Result
                {
                    Error = "この記事はすでに判定済みです。「判定した記事」から削除してからもう一度試してください。",
                };
            }

            //公開日時は分からないので判定した時刻を入れる。一覧の先頭に出てきて見つけやすい
            RssItem item = new(articleUrl, articleTitle ?? "", DateTime.Now, articleSummary);

            GeminiLiveryJudge.JudgeResult? judged =
                await GeminiLiveryJudge.JudgeWithRetryAsync(apiKey, LiveryNewsConstant.GEMINI_MODEL, item);
            if (judged == null)
            {
                return new Result { Error = "Geminiの判定を取得できませんでした。ログ（journalctl）を確認してください。" };
            }

            Result result = new() { Response = judged.Response, Raw = judged.Raw };

            if (!save)
            {
                result.Message = judged.Response.SpecialLivery
                    ? "特別塗装の記事と判定されました。保存はしていません。"
                    : "特別塗装の記事ではないと判定されました。保存はしていません。";
                return result;
            }

            (LiveryNews news, List<LiveryNewsCandidate> created) =
                await LiveryNewsChecker.SaveJudgementAsync(context, item, judged);

            List<LiveryNewsCandidate> pending = [.. created.Where(c => c.Status == LiveryNewsStatus.PENDING)];
            if (pending.Count > 0)
            {
                await LiveryNewsChecker.NotifyAsync(pending, news);
            }

            result.Saved = true;
            result.PendingCount = pending.Count;
            result.Message = news.SpecialLivery
                ? $"特別塗装の記事と判定し、候補を{created.Count}件（未処理{pending.Count}件）作りました。"
                : "特別塗装の記事ではないと判定されたため、候補は作られていません。";
            return result;
        }

        /// <summary>
        /// DBを見ないで分かる範囲の検査。入力の形だけを見る。
        /// </summary>
        public static string? Validate(string articleUrl, string? articleSummary, bool save)
        {
            if (articleUrl.Length == 0 && string.IsNullOrWhiteSpace(articleSummary))
            {
                return "記事URLか、本文・概要のどちらかを入れてください。";
            }
            if (articleUrl.Length > 0
                && !articleUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !articleUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "記事URLはhttpまたはhttpsから始まる形で入れてください。";
            }
            if (save && articleUrl.Length == 0)
            {
                //候補の一覧から記事を開けるようにしておきたいので、保存するときはURLが要る
                return "候補を作るには記事URLが必要です。判定だけなら本文の貼り付けでも試せます。";
            }
            return null;
        }

        /// <summary>
        /// Geminiを通さずに未処理の候補を1件作る。
        /// 承認したときの反映（aircraftの更新・更新履歴・写真の取り直し）だけを確かめたいとき用で、
        /// 何度でもすぐ作れる。Slackへは通知しない。
        /// </summary>
        public static async Task<Result> CreateManualAsync(
            JAFleetContext context, string? registration, string? liveryName)
        {
            string? reg = LiveryNewsChecker.NormalizeRegistration(registration);
            if (reg == null)
            {
                return new Result { Error = "レジを「JA」から始まる形で入力してください。" };
            }

            string? livery = LiveryNewsChecker.Trim(liveryName, LiveryNewsConstant.MAX_LIVERY_LENGTH);
            if (livery == null)
            {
                return new Result { Error = "塗装名を入力してください。" };
            }

            DateTime now = DateTime.Now;
            LiveryNews news = new()
            {
                //URLは一意なので時刻で作る。httpで始まらないため画面ではリンクにならない
                NewsUrl = $"{MANUAL_URL_PREFIX}{now:yyyyMMddHHmmssfff}",
                Title = "（手動で作った候補）",
                PublishedAt = now,
                CheckedAt = now,
                SpecialLivery = true,
                Reason = "画面から手動で作った候補です。Geminiの判定は通していません。",
            };
            context.LiveryNewsList.Add(news);
            await context.SaveChangesAsync();

            string? current = await context.Aircrafts.AsNoTracking()
                .Where(a => a.RegistrationNumber == reg)
                .Select(a => a.SpecialLivery)
                .FirstOrDefaultAsync();

            //試すために作るものなので、いまと同じ内容でも未処理のまま置く
            LiveryNewsCandidate candidate = new()
            {
                NewsId = news.NewsId,
                RegistrationNumber = reg,
                LiveryName = livery,
                CurrentLivery = current,
                Status = LiveryNewsStatus.PENDING,
                CreatedAt = now,
            };
            context.LiveryNewsCandidates.Add(candidate);
            await context.SaveChangesAsync();

            return new Result
            {
                Saved = true,
                PendingCount = 1,
                Message = $"{reg}の候補（{livery}）を作りました。未処理の候補から承認を試せます。",
            };
        }

        /// <summary>
        /// 判定した記事を消す。お試しで作ったものの片付けと、プロンプトを直したあとの再判定に使う。
        /// 未処理の候補は一緒に消すが、承認・却下済みの候補が付いている記事は消させない
        /// （何を承認したのかが分からなくなるため）。
        /// </summary>
        public static async Task<Result> DeleteNewsAsync(JAFleetContext context, int newsId)
        {
            LiveryNews? news = await context.LiveryNewsList.FirstOrDefaultAsync(n => n.NewsId == newsId);
            if (news == null)
            {
                return new Result { Error = "記事が見つかりません。" };
            }

            List<LiveryNewsCandidate> candidates = await context.LiveryNewsCandidates
                .Where(c => c.NewsId == newsId)
                .ToListAsync();

            if (candidates.Any(c => c.Status is LiveryNewsStatus.APPROVED or LiveryNewsStatus.REJECTED))
            {
                return new Result { Error = "承認・却下した候補が付いている記事は消せません。" };
            }

            context.LiveryNewsCandidates.RemoveRange(candidates);
            context.LiveryNewsList.Remove(news);
            await context.SaveChangesAsync();

            return new Result
            {
                Message = candidates.Count == 0
                    ? "記事を消しました。もう一度判定できます。"
                    : $"記事と候補{candidates.Count}件を消しました。もう一度判定できます。",
            };
        }
    }
}
