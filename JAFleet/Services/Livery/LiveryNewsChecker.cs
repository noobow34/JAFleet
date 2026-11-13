using EnumStringValues;
using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using Microsoft.EntityFrameworkCore;
using Noobow.Commons.Constants;
using Noobow.Commons.Utils;

namespace JAFleet.Services.Livery
{
    /// <summary>
    /// Aviation WireのRSSを取得し、まだ見ていない記事をGeminiに判定させて
    /// 特別塗装の更新候補（livery_news_candidate）を作る。
    /// 日次ジョブ（CheckSpecialLiveryNewsJob）と、承認画面の「いま確認する」から呼ぶ。
    /// </summary>
    public static class LiveryNewsChecker
    {
        /// <summary>画面にそのまま返せる実行結果</summary>
        public sealed class Result
        {
            /// <summary>Geminiが判定を返した記事数</summary>
            public int CheckedCount { get; set; }

            /// <summary>特別塗装の記事と判定された件数</summary>
            public int LiveryCount { get; set; }

            /// <summary>作った候補のうち、未処理として画面に出る件数</summary>
            public int PendingCount { get; set; }

            public string? Message { get; set; }

            public string? Error { get; set; }
        }

        /// <summary>候補とその記事。Slack通知の組み立てに使う</summary>
        private record PendingCandidate(LiveryNewsCandidate Candidate, LiveryNews News);

        /// <summary>
        /// いま実行中かどうか。記事ごとに数秒待つため1回が数分かかることがあり、
        /// 日次ジョブと画面の「いま確認する」が重なると同じ記事を二重に投げてしまうため見張る。
        /// </summary>
        public static bool Processing { get; private set; }

        public static async Task<Result> ExecuteAsync(JAFleetContext context)
        {
            if (Processing)
            {
                return new Result { Error = "すでに実行中です。終わるまで待ってください。" };
            }

            Processing = true;
            try
            {
                return await ExecuteCoreAsync(context);
            }
            finally
            {
                Processing = false;
            }
        }

        private static async Task<Result> ExecuteCoreAsync(JAFleetContext context)
        {
            string apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
            if (string.IsNullOrEmpty(apiKey))
            {
                return new Result { Error = "GEMINI_API_KEYが設定されていないため実行できません。" };
            }

            IReadOnlyList<RssItem> items;
            try
            {
                items = await AviationWireRss.FetchAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LiveryNews] RSSの取得に失敗しました: {ex.Message}");
                return new Result { Error = $"RSSの取得に失敗しました。({ex.Message})" };
            }

            //チェック済みの記事は二度と投げない。同じ記事を何度もGeminiに判定させないため
            List<string> urls = [.. items.Select(i => i.Url)];
            HashSet<string> known = [.. context.LiveryNewsList.AsNoTracking()
                .Where(n => urls.Contains(n.NewsUrl))
                .Select(n => n.NewsUrl)];

            //古い記事から順に処理する。途中で落ちても次回は残りだけを見ればよい
            List<RssItem> targets = [.. items
                .Where(i => !known.Contains(i.Url))
                .OrderBy(i => i.PublishedAt ?? DateTime.MinValue)
                .Take(LiveryNewsConstant.MAX_ITEMS_PER_RUN)];

            Console.WriteLine($"[LiveryNews] RSS={items.Count}件 未チェック={targets.Count}件");

            Result result = new();
            List<PendingCandidate> pending = [];

            for (int i = 0; i < targets.Count; i++)
            {
                RssItem item = targets[i];
                if (i > 0)
                {
                    await Task.Delay(LiveryNewsConstant.DELAY_BETWEEN_ITEMS);
                }

                GeminiLiveryJudge.JudgeResult? judged =
                    await GeminiLiveryJudge.JudgeWithRetryAsync(apiKey, LiveryNewsConstant.GEMINI_MODEL, item);
                if (judged == null)
                {
                    //判定できなかった記事は印を付けずに次回へ回す
                    continue;
                }

                try
                {
                    (LiveryNews news, List<LiveryNewsCandidate> created) = await SaveJudgementAsync(context, item, judged);
                    result.CheckedCount++;
                    if (news.SpecialLivery)
                    {
                        result.LiveryCount++;
                    }
                    pending.AddRange(created
                        .Where(c => c.Status == LiveryNewsStatus.PENDING)
                        .Select(c => new PendingCandidate(c, news)));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LiveryNews] 保存に失敗しました（{item.Url}）: {ex.Message}");
                    //1記事分の保存が落ちても残りは続ける。積んだ変更は捨てる
                    foreach (var entry in context.ChangeTracker.Entries().ToList())
                    {
                        entry.State = EntityState.Detached;
                    }
                }
            }

            result.PendingCount = pending.Count;
            result.Message = $"記事{result.CheckedCount}件を判定し、特別塗装{result.LiveryCount}件／未処理の候補{result.PendingCount}件を作りました。";
            Console.WriteLine($"[LiveryNews] {result.Message}");

            if (pending.Count > 0)
            {
                await NotifySlackAsync(pending);
            }

            return result;
        }

        /// <summary>
        /// 判定結果を1記事分保存する。特別塗装なら対象機ごとに候補を作る。
        /// 登録済みの特別塗装と同じ内容なら、作りはするが差分なしとして処理済みにする。
        /// お試し判定（<see cref="LiveryNewsTrial"/>）も同じ処理を通す。
        /// </summary>
        internal static async Task<(LiveryNews News, List<LiveryNewsCandidate> Created)> SaveJudgementAsync(
            JAFleetContext context, RssItem item, GeminiLiveryJudge.JudgeResult judged)
        {
            DateTime now = DateTime.Now;
            LiveryNews news = new()
            {
                NewsUrl = item.Url,
                Title = Trim(item.Title, 512),
                PublishedAt = item.PublishedAt,
                CheckedAt = now,
                SpecialLivery = judged.Response.SpecialLivery,
                Reason = Trim(judged.Response.Reason, 1000),
                GeminiResult = judged.Raw,
            };
            context.LiveryNewsList.Add(news);
            await context.SaveChangesAsync();

            if (!news.SpecialLivery)
            {
                return (news, []);
            }

            //対象機が挙がらなかった場合も、レジが空の候補を1件作って人が入れられるようにする
            List<GeminiLiveryTarget> targets = judged.Response.Targets is { Count: > 0 }
                ? judged.Response.Targets
                : [new GeminiLiveryTarget()];

            List<LiveryNewsCandidate> created = [];
            foreach (GeminiLiveryTarget target in targets)
            {
                string? reg = NormalizeRegistration(target.Registration);
                string? livery = Trim(target.LiveryName, LiveryNewsConstant.MAX_LIVERY_LENGTH);

                string? current = reg == null
                    ? null
                    : await context.Aircrafts.AsNoTracking()
                        .Where(a => a.RegistrationNumber == reg)
                        .Select(a => a.SpecialLivery)
                        .FirstOrDefaultAsync();

                //同じ内容が既に入っているなら知らせる必要がない（同じ塗装が別の記事で何度も出るため）
                bool noDiff = reg != null && livery != null && Same(current, livery);

                LiveryNewsCandidate candidate = new()
                {
                    NewsId = news.NewsId,
                    RegistrationNumber = reg,
                    LiveryName = livery,
                    CurrentLivery = current,
                    Status = noDiff ? LiveryNewsStatus.NO_DIFF : LiveryNewsStatus.PENDING,
                    CreatedAt = now,
                    ResolvedAt = noDiff ? now : null,
                };
                context.LiveryNewsCandidates.Add(candidate);
                created.Add(candidate);
            }

            await context.SaveChangesAsync();
            return (news, created);
        }

        /// <summary>
        /// 記事中の書き方（小文字・全角・空白やハイフン入り）を揃える。
        /// JAで始まらないものや、レジの長さに収まらないものは読み取れなかったものとして落とす
        /// （「JA926A（787-9）」のように型式までくっついてきた場合など）。
        /// 落としても候補は作られるので、画面で人が入れ直せる。
        /// </summary>
        public static string? NormalizeRegistration(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string reg = new(value
                .Normalize(System.Text.NormalizationForm.FormKC)
                .Where(char.IsLetterOrDigit)
                .ToArray());
            reg = reg.ToUpperInvariant();

            //JAのあとは3〜5文字（JA01XX、JA8961 など）
            return reg.StartsWith("JA", StringComparison.Ordinal) && reg.Length is >= 5 and <= 7 ? reg : null;
        }

        /// <summary>表記ゆれ（前後の空白・全角半角）を吸収して比べる</summary>
        private static bool Same(string? a, string? b)
        {
            static string Key(string? s) => (s ?? "").Trim().Normalize(System.Text.NormalizationForm.FormKC);
            return string.Equals(Key(a), Key(b), StringComparison.Ordinal);
        }

        internal static string? Trim(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            string trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        /// <summary>
        /// 1記事分の候補ができたことをSlackへ知らせる。お試し判定から使う
        /// （本番と同じ通知が飛ぶことを確かめられるようにするため）。
        /// </summary>
        internal static Task NotifyAsync(IEnumerable<LiveryNewsCandidate> candidates, LiveryNews news)
            => NotifySlackAsync([.. candidates.Select(c => new PendingCandidate(c, news))]);

        /// <summary>未処理の候補ができたことをSlackへ知らせる。承認画面へのリンクを付ける</summary>
        private static async Task NotifySlackAsync(List<PendingCandidate> pending)
        {
            try
            {
                IEnumerable<string> lines = pending.Select(p =>
                {
                    string reg = p.Candidate.RegistrationNumber ?? "レジ不明";
                    string livery = p.Candidate.LiveryName ?? "塗装名不明";
                    string title = p.News.Title ?? "記事";
                    return $"• {reg}　{livery}（<{p.News.NewsUrl}|{title}>）";
                });

                string text = $":art: *特別塗装機のニュースを見つけました（{pending.Count}件）*\n"
                    + string.Join("\n", lines) + "\n"
                    + $"<{LiveryNewsConstant.SITE_URL}/LiveryNews?fromAdmin=true|承認画面を開く>";

                await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LiveryNews] Slack通知に失敗しました: {ex.Message}");
            }
        }
    }
}
