using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using JAFleet.Models;
using JAFleet.Services.Livery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Controllers
{
    /// <summary>
    /// 特別塗装ニュースの承認画面。
    /// 日次ジョブ（CheckSpecialLiveryNewsJob）がAviation WireのRSSをGeminiに判定させて作った
    /// 更新候補を並べ、承認したものだけaircraft.special_liveryへ反映する。
    /// </summary>
    public class LiveryNewsController : Controller
    {
        private const string TITLE = "特別塗装ニュース";

        /// <summary>処理済みの候補と判定した記事を出す件数。古い分は画面には出さない</summary>
        private const int HISTORY_COUNT = 30;

        private readonly JAFleetContext _context;
        private readonly IServiceScopeFactory _services;

        public LiveryNewsController(JAFleetContext context, IServiceScopeFactory services)
        {
            _context = context;
            _services = services;
        }

        public async Task<IActionResult> IndexAsync([FromQuery] bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            LiveryNewsModel model = await BuildModelAsync(fromAdmin);
            //確認を始めた直後は画面を作り直すだけなので、知らせはTempDataで持ち回る
            model.Message = TempData["LiveryNewsMessage"] as string;
            model.ErrorMessage = TempData["LiveryNewsError"] as string;
            return View(model);
        }

        /// <summary>候補を承認してaircraftへ反映する。レジと塗装名は画面で直した値を使う</summary>
        [HttpPost]
        public async Task<IActionResult> ApproveAsync(int candidateId, string? registration, string? liveryName, bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            LiveryNewsStore.Result result;
            try
            {
                result = await LiveryNewsStore.ApproveAsync(_context, candidateId, registration, liveryName);
            }
            catch (Exception ex)
            {
                result = new LiveryNewsStore.Result { Error = $"反映できませんでした。({ex.Message})", ErrorCandidateId = candidateId };
            }

            LiveryNewsModel model = await BuildModelAsync(fromAdmin);
            model.Message = result.Message;
            model.ErrorMessage = result.Error;
            model.ErrorCandidateId = result.ErrorCandidateId;

            //反映した機体の写真も取り直す（単票編集と同じ）
            if (result.Error == null && !string.IsNullOrEmpty(registration))
            {
                string? reg = LiveryNewsChecker.NormalizeRegistration(registration);
                if (reg != null)
                {
                    _ = HttpClientManager.GetInstance().GetStringAsync($"http://localhost:5000/Aircraft/Photo/{reg}?force=true");
                }
            }

            return View("Index", model);
        }

        /// <summary>候補を却下する。aircraftは変えない</summary>
        [HttpPost]
        public async Task<IActionResult> RejectAsync(int candidateId, bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            LiveryNewsStore.Result result = await LiveryNewsStore.RejectAsync(_context, candidateId);

            LiveryNewsModel model = await BuildModelAsync(fromAdmin);
            model.Message = result.Message;
            model.ErrorMessage = result.Error;
            model.ErrorCandidateId = result.ErrorCandidateId;
            return View("Index", model);
        }

        /// <summary>
        /// ジョブを待たずにいまRSSを確認する。
        /// 記事ごとに数秒待つため1回が数分かかることがあるので、裏で動かしてすぐ画面を返す
        /// （バッチ手動実行と同じ考え方）。
        /// </summary>
        [HttpPost]
        public IActionResult Check(bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            if (LiveryNewsChecker.Processing)
            {
                TempData["LiveryNewsError"] = "すでに確認が動いています。終わるまで待ってください。";
            }
            else if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GEMINI_API_KEY")))
            {
                TempData["LiveryNewsError"] = "GEMINI_API_KEYが設定されていないため確認できません。";
            }
            else
            {
                _ = Task.Run(async () =>
                {
                    using IServiceScope scope = _services.CreateScope();
                    using JAFleetContext? context = scope.ServiceProvider.GetService<JAFleetContext>();
                    await LiveryNewsChecker.ExecuteAsync(context!);
                });
                TempData["LiveryNewsMessage"] = "RSSの確認を始めました。数分かかることがあります。"
                    + "終わったらSlackに通知が飛ぶので、この画面を読み直してください。";
            }

            return Redirect(fromAdmin ? "/LiveryNews?fromAdmin=true" : "/LiveryNews");
        }

        /// <summary>
        /// 記事1本をその場で判定する。saveがfalseなら判定結果を見るだけ、trueなら本番と同じく
        /// 候補を作ってSlackへ通知する。ニュースが出るのを待たずに試すための入口。
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TrialAsync(string? url, string? title, string? summary, bool save, bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            LiveryNewsTrial.Result result;
            try
            {
                result = await LiveryNewsTrial.JudgeAsync(_context, url, title, summary, save);
            }
            catch (Exception ex)
            {
                result = new LiveryNewsTrial.Result { Error = $"判定できませんでした。({ex.Message})" };
            }

            LiveryNewsModel model = await BuildModelAsync(fromAdmin);
            model.Message = result.Message;
            model.ErrorMessage = result.Error;
            //判定の中身を見るため、保存しなかったときだけ結果を画面に出す
            model.Trial = result.Saved ? null : result;
            model.TrialUrl = url;
            model.TrialTitle = title;
            model.TrialSummary = summary;
            return View("Index", model);
        }

        /// <summary>Geminiを通さずに候補を1件作る。承認したときの反映だけを試したいとき用</summary>
        [HttpPost]
        public async Task<IActionResult> ManualAsync(string? registration, string? liveryName, bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            LiveryNewsTrial.Result result = await LiveryNewsTrial.CreateManualAsync(_context, registration, liveryName);

            LiveryNewsModel model = await BuildModelAsync(fromAdmin);
            model.Message = result.Message;
            model.ErrorMessage = result.Error;
            if (result.Error != null)
            {
                model.TrialRegistration = registration;
                model.TrialLiveryName = liveryName;
            }
            return View("Index", model);
        }

        /// <summary>判定した記事を消す。お試しの片付けと、プロンプトを直したあとの再判定に使う</summary>
        [HttpPost]
        public async Task<IActionResult> DeleteNewsAsync(int newsId, bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            LiveryNewsTrial.Result result = await LiveryNewsTrial.DeleteNewsAsync(_context, newsId);

            LiveryNewsModel model = await BuildModelAsync(fromAdmin);
            model.Message = result.Message;
            model.ErrorMessage = result.Error;
            return View("Index", model);
        }

        private async Task<LiveryNewsModel> BuildModelAsync(bool fromAdmin)
        {
            List<LiveryNewsCandidate> candidates = await _context.LiveryNewsCandidates.AsNoTracking()
                .Where(c => c.Status == LiveryNewsStatus.PENDING)
                .OrderByDescending(c => c.CandidateId)
                .ToListAsync();

            List<LiveryNewsCandidate> resolved = await _context.LiveryNewsCandidates.AsNoTracking()
                .Where(c => c.Status != LiveryNewsStatus.PENDING)
                .OrderByDescending(c => c.CandidateId)
                .Take(HISTORY_COUNT)
                .ToListAsync();

            //候補が指している記事だけを引く
            List<int> newsIds = [.. candidates.Select(c => c.NewsId).Concat(resolved.Select(c => c.NewsId)).Distinct()];
            Dictionary<int, LiveryNews> news = await _context.LiveryNewsList.AsNoTracking()
                .Where(n => newsIds.Contains(n.NewsId))
                .ToDictionaryAsync(n => n.NewsId, n => n);

            //レジごとのいまの機体情報。承認できるかの判断と、並べて見せるために引く
            List<string> regs = [.. candidates.Concat(resolved)
                .Select(c => c.RegistrationNumber)
                .Where(r => r != null)
                .Distinct()
                .Cast<string>()];
            Dictionary<string, AircraftView> aircrafts = await _context.AircraftViews.AsNoTracking()
                .Where(a => regs.Contains(a.RegistrationNumber))
                .ToDictionaryAsync(a => a.RegistrationNumber, a => a);

            List<LiveryNews> recent = await _context.LiveryNewsList.AsNoTracking()
                .OrderByDescending(n => n.NewsId)
                .Take(HISTORY_COUNT)
                .ToListAsync();
            List<int> recentIds = [.. recent.Select(n => n.NewsId)];
            Dictionary<int, int> candidateCounts = await _context.LiveryNewsCandidates.AsNoTracking()
                .Where(c => recentIds.Contains(c.NewsId))
                .GroupBy(c => c.NewsId)
                .Select(g => new { NewsId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.NewsId, x => x.Count);

            //承認・却下した候補が付いている記事は消させない（何を承認したのか分からなくなるため）
            HashSet<int> locked = [.. await _context.LiveryNewsCandidates.AsNoTracking()
                .Where(c => recentIds.Contains(c.NewsId)
                    && (c.Status == LiveryNewsStatus.APPROVED || c.Status == LiveryNewsStatus.REJECTED))
                .Select(c => c.NewsId)
                .Distinct()
                .ToListAsync()];

            return new LiveryNewsModel
            {
                Title = TITLE,
                IsAdmin = true,
                FromAdmin = fromAdmin,
                Pending = [.. candidates.Select(c => ToRow(c, news, aircrafts))],
                Resolved = [.. resolved.Select(c => ToRow(c, news, aircrafts))],
                RecentNews = [.. recent.Select(n => new LiveryNewsRow
                {
                    NewsId = n.NewsId,
                    Title = n.Title,
                    NewsUrl = n.NewsUrl,
                    PublishedAt = n.PublishedAt,
                    CheckedAt = n.CheckedAt,
                    SpecialLivery = n.SpecialLivery,
                    Reason = n.Reason,
                    CandidateCount = candidateCounts.TryGetValue(n.NewsId, out int count) ? count : 0,
                    CanDelete = !locked.Contains(n.NewsId),
                    HasLink = HasLink(n.NewsUrl),
                })],
                LastCheckedAt = recent.Count == 0 ? null : recent.Max(n => n.CheckedAt),
                CheckedNewsCount = await _context.LiveryNewsList.CountAsync(),
                GeminiConfigured = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
                Processing = LiveryNewsChecker.Processing,
            };
        }

        /// <summary>手動で作った候補のURL（manual:…）はリンクにできない</summary>
        private static bool HasLink(string? url)
            => url != null && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        private static LiveryCandidateRow ToRow(
            LiveryNewsCandidate candidate,
            Dictionary<int, LiveryNews> news,
            Dictionary<string, AircraftView> aircrafts)
        {
            AircraftView? aircraft = candidate.RegistrationNumber != null
                && aircrafts.TryGetValue(candidate.RegistrationNumber, out AircraftView? found) ? found : null;
            LiveryNews? n = news.TryGetValue(candidate.NewsId, out LiveryNews? hit) ? hit : null;

            return new LiveryCandidateRow
            {
                CandidateId = candidate.CandidateId,
                RegistrationNumber = candidate.RegistrationNumber,
                LiveryName = candidate.Status == LiveryNewsStatus.APPROVED
                    ? candidate.AppliedLivery
                    : candidate.LiveryName,
                CurrentLivery = candidate.CurrentLivery,
                NowLivery = aircraft?.SpecialLivery,
                AircraftFound = aircraft != null,
                AirlineName = aircraft?.AirlineNameJpShort,
                TypeName = aircraft?.TypeDetailName ?? aircraft?.TypeName,
                Status = candidate.Status,
                CreatedAt = candidate.CreatedAt,
                ResolvedAt = candidate.ResolvedAt,
                NewsTitle = n?.Title,
                NewsUrl = n?.NewsUrl,
                NewsHasLink = HasLink(n?.NewsUrl),
                PublishedAt = n?.PublishedAt,
                Reason = n?.Reason,
            };
        }
    }
}
