using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using JAFleet.Models;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Services
{
    /// <summary>
    /// access_logを集計する。
    ///
    /// access_logはサーバのlog_process.shが毎晩31日より古い行を消しているため、
    /// ここで遡れるのも31日分まで。それより前を見たい場合は集計結果を別テーブルへ
    /// 退避する仕組みが要る。
    ///
    /// 集計はすべてDB側のGROUP BYで行い、アプリへは集計済みの行だけを持ってくる。
    /// </summary>
    public static class AccessStatsService
    {
        /// <summary>画面で選べる日数。access_logの保持期間（31日）を超える値は置かない</summary>
        public static readonly int[] DAY_CHOICES = [1, 3, 7, 14, 30];

        private const int TOP_COUNT = 20;
        //平均応答時間は件数が少ないと1件の外れ値で順位が決まってしまうので、
        //ある程度たたかれているパスだけを「遅いページ」に載せる
        private const int SLOW_MIN_PAGEVIEWS = 20;

        /// <summary>
        /// 画面の枠だけを作る。DBには触らないので即座に返せる。
        /// 中身（重い集計）は画面から/AccessStats/Dataを呼んで後から差し込む
        /// </summary>
        public static AccessStatsModel BuildShell(int days, bool includeBot, bool includeAdmin, bool fromAdmin)
        {
            if (!DAY_CHOICES.Contains(days))
            {
                days = 7;
            }

            return new AccessStatsModel
            {
                Title = "アクセス集計",
                IsAdmin = true,
                FromAdmin = fromAdmin,
                Days = days,
                IncludeBot = includeBot,
                IncludeAdmin = includeAdmin,
                From = DateTime.Now.Date.AddDays(-(days - 1)),
                To = DateTime.Now,
            };
        }

        public static async Task<AccessStatsModel> BuildAsync(JAFleetContext context, int days, bool includeBot, bool includeAdmin, bool fromAdmin)
        {
            AccessStatsModel model = BuildShell(days, includeBot, includeAdmin, fromAdmin);
            days = model.Days;
            DateTime from = model.From;

            //recordedは記録された全リクエスト、logsはそのうち人が見るページの分だけ。
            //集計は基本ページだけを数え、落とした分の件数だけ画面に出して隠れないようにする
            IQueryable<AccessLog> recorded = Filter(context, from, includeBot, includeAdmin);
            IQueryable<AccessLog> logs = PageOnly(recorded);

            long recordedCount = await recorded.LongCountAsync();

            var totals = await logs
                .GroupBy(l => 1)
                .Select(g => new
                {
                    PageViews = (long)g.Count(),
                    AvgResponseMs = g.Average(l => (double?)l.ResponseTime) ?? 0,
                    ErrorCount = (long)g.Count(l => l.ResponseCode >= 400),
                })
                .FirstOrDefaultAsync();

            int visitors = await logs.Where(l => l.VisitorId != null).Select(l => l.VisitorId).Distinct().CountAsync();
            int ips = await logs.Where(l => l.RequestIp != null).Select(l => l.RequestIp).Distinct().CountAsync();

            //パス別は「人気ページ」「ページ種別」「レジ別」「遅いページ」の4つに使い回す。
            //パスの種類はページ数で頭打ちになるので、まとめてメモリへ持ってきてよい
            var paths = await logs
                .GroupBy(l => l.RequestPath)
                .Select(g => new
                {
                    Path = g.Key,
                    PageViews = (long)g.Count(),
                    AvgResponseMs = g.Average(l => (double?)l.ResponseTime) ?? 0,
                })
                .ToListAsync();

            model.PageViews = totals?.PageViews ?? 0;
            model.AvgResponseMs = totals?.AvgResponseMs ?? 0;
            model.ErrorCount = totals?.ErrorCount ?? 0;
            model.Visitors = visitors;
            model.Ips = ips;
            model.NonPageCount = recordedCount - (totals?.PageViews ?? 0);
            model.Daily = await BuildDailyAsync(logs, from, days);
            model.Hourly = await BuildHourlyAsync(logs);
            model.Controllers = await BuildControllersAsync(logs);
            model.Referers = await BuildReferersAsync(logs);
            model.Devices = await BuildDevicesAsync(logs);
            model.StatusCodes = await BuildStatusCodesAsync(logs);

            model.TopPaths = paths
                .OrderByDescending(p => p.PageViews)
                .Take(TOP_COUNT)
                .Select(p => new PathStat { Path = p.Path ?? string.Empty, PageViews = p.PageViews, AvgResponseMs = p.AvgResponseMs })
                .ToList();

            model.SlowPaths = paths
                .Where(p => p.PageViews >= SLOW_MIN_PAGEVIEWS)
                .OrderByDescending(p => p.AvgResponseMs)
                .Take(10)
                .Select(p => new PathStat { Path = p.Path ?? string.Empty, PageViews = p.PageViews, AvgResponseMs = p.AvgResponseMs })
                .ToList();

            model.TopRegistrations = paths
                .Select(p => new { Registration = AccessPath.DetailRegistration(p.Path), p.PageViews })
                .Where(p => p.Registration != null)
                .GroupBy(p => p.Registration!)
                .Select(g => new CountStat { Name = g.Key, Count = g.Sum(p => p.PageViews) })
                .OrderByDescending(c => c.Count)
                .Take(TOP_COUNT)
                .ToList();

            return model;
        }

        /// <summary>
        /// 集計対象の絞り込み。管理者自身のアクセスとボットはどちらも既定で外し、画面から含められる。
        /// ボットはUser-Agentに含まれる語で外しており、SQLのlower()とLIKEに落ちる
        /// </summary>
        private static IQueryable<AccessLog> Filter(JAFleetContext context, DateTime from, bool includeBot, bool includeAdmin)
        {
            IQueryable<AccessLog> logs = context.AccessLogs.AsNoTracking()
                .Where(l => l.RequestTime >= from);

            if (!includeAdmin)
            {
                //is_adminがNULLの行（列を足す前の記録）は管理者と分からないので残す
                logs = logs.Where(l => l.IsAdmin != true);
            }

            if (!includeBot)
            {
                logs = logs.Where(l => l.UserAgent != null && l.UserAgent != "");
                foreach (string keyword in UserAgentUtil.BOT_KEYWORDS)
                {
                    logs = logs.Where(l => !l.UserAgent!.ToLower().Contains(keyword));
                }
            }

            return logs;
        }

        /// <summary>
        /// 人が見るページへのアクセスだけに絞る。
        /// 静的ファイルやどのルートにも当たらなかったリクエストはroute_keyがnullなので落ち、
        /// JSON APIやAJAX用のコントローラーは名前で落とす。
        /// route_keyを記録する前の行もnullなので、古い記録は対象外になる
        /// </summary>
        private static IQueryable<AccessLog> PageOnly(IQueryable<AccessLog> logs)
        {
            IQueryable<AccessLog> pages = logs.Where(l => l.RouteKey != null);
            foreach (string controller in AccessPath.NON_PAGE_CONTROLLERS)
            {
                //"Airline/"のように区切りまで含めて比べる。含めないと"AirlineGroup"まで落ちる
                string prefix = controller + "/";
                pages = pages.Where(l => !l.RouteKey!.StartsWith(prefix) && l.RouteKey != controller);
            }

            return pages.Concat(Legacy(logs));
        }

        /// <summary>
        /// 移行措置。route_keyを記録する前の行をパスから拾う。
        /// ページのパスには拡張子が付かない（"/"、"/AD/JA226P"、"/Search"）のに対し、
        /// 静的ファイルには必ず付くので、ピリオドの有無だけで大半を見分けられる。
        /// デプロイから1か月たってaccess_logが入れ替わったら、このメソッドごと消してよい
        /// </summary>
        private static IQueryable<AccessLog> Legacy(IQueryable<AccessLog> logs)
        {
            IQueryable<AccessLog> legacy = logs
                .Where(l => l.RouteKey == null && l.RequestPath != null && !l.RequestPath.Contains("."));

            foreach (string prefix in AccessPath.LEGACY_NON_PAGE_PREFIXES)
            {
                legacy = legacy.Where(l => !l.RequestPath!.ToLower().StartsWith(prefix));
            }
            return legacy;
        }

        /// <summary>コントローラー別のPV。アクションはまとめる（機体詳細の派生パスなどを1つに数える）</summary>
        private static async Task<List<CountStat>> BuildControllersAsync(IQueryable<AccessLog> logs)
        {
            var routes = await logs
                .Where(l => l.RouteKey != null)
                .GroupBy(l => l.RouteKey)
                .Select(g => new { RouteKey = g.Key, PageViews = (long)g.Count() })
                .ToListAsync();

            var named = routes.Select(r => new { Name = AccessPath.DisplayName(r.RouteKey), r.PageViews });

            //移行措置。route_keyが無い行はパスの先頭から呼び名を当てる。1か月たてばこの塊は消してよい
            var legacy = await logs
                .Where(l => l.RouteKey == null)
                .GroupBy(l => l.RequestPath)
                .Select(g => new { Path = g.Key, PageViews = (long)g.Count() })
                .ToListAsync();

            return named
                .Concat(legacy.Select(l => new { Name = AccessPath.LegacyDisplayName(l.Path), l.PageViews }))
                .GroupBy(r => r.Name)
                .Select(g => new CountStat { Name = g.Key, Count = g.Sum(r => r.PageViews) })
                .OrderByDescending(c => c.Count)
                .ToList();
        }

        /// <summary>
        /// 日別のPVと訪問者数。訪問者数はCOUNT(DISTINCT)ではなく
        /// 「日付とIDの組を一意にしてから数える」形にして、確実にSQLへ落とす
        /// </summary>
        private static async Task<List<DailyStat>> BuildDailyAsync(IQueryable<AccessLog> logs, DateTime from, int days)
        {
            var pageViews = await logs
                .GroupBy(l => l.RequestTime.Date)
                .Select(g => new { Date = g.Key, PageViews = (long)g.Count() })
                .ToListAsync();

            //「日付とIDの組」をDB側で一意にしてから数える。
            //行数は（日数×その日の訪問者数）で収まるので、数えるのはメモリ上で足りる
            var visitorDays = await logs
                .Where(l => l.VisitorId != null)
                .Select(l => new { l.RequestTime.Date, l.VisitorId })
                .Distinct()
                .ToListAsync();
            var visitors = visitorDays
                .GroupBy(x => x.Date)
                .Select(g => new { Date = g.Key, Visitors = g.Count() })
                .ToList();

            //アクセスが無かった日もグラフに出したいので、期間の日付を先に並べて突き合わせる
            return Enumerable.Range(0, days)
                .Select(i => from.AddDays(i))
                .Where(date => date <= DateTime.Now.Date)
                .Select(date => new DailyStat
                {
                    Date = date,
                    PageViews = pageViews.FirstOrDefault(p => p.Date == date)?.PageViews ?? 0,
                    Visitors = visitors.FirstOrDefault(v => v.Date == date)?.Visitors ?? 0,
                })
                .ToList();
        }

        private static async Task<List<HourlyStat>> BuildHourlyAsync(IQueryable<AccessLog> logs)
        {
            var hours = await logs
                .GroupBy(l => l.RequestTime.Hour)
                .Select(g => new { Hour = g.Key, PageViews = (long)g.Count() })
                .ToListAsync();

            return Enumerable.Range(0, 24)
                .Select(hour => new HourlyStat
                {
                    Hour = hour,
                    PageViews = hours.FirstOrDefault(h => h.Hour == hour)?.PageViews ?? 0,
                })
                .ToList();
        }

        /// <summary>外部サイトからの流入元。自サイト内の遷移は外す</summary>
        private static async Task<List<CountStat>> BuildReferersAsync(IQueryable<AccessLog> logs)
        {
            return await logs
                .Where(l => l.Referer != null && l.Referer != ""
                         && !l.Referer.Contains("ja-fleet")
                         && !l.Referer.Contains("localhost"))
                .GroupBy(l => l.Referer!)
                .Select(g => new CountStat { Name = g.Key, Count = (long)g.Count() })
                .OrderByDescending(c => c.Count)
                .Take(10)
                .ToListAsync();
        }

        private static async Task<List<CountStat>> BuildDevicesAsync(IQueryable<AccessLog> logs)
        {
            return await logs
                .GroupBy(UserAgentUtil.DeviceKey)
                .Select(g => new CountStat { Name = g.Key, Count = (long)g.Count() })
                .OrderByDescending(c => c.Count)
                .ToListAsync();
        }

        private static async Task<List<CountStat>> BuildStatusCodesAsync(IQueryable<AccessLog> logs)
        {
            //コードの文字列化はメモリ上で行う。int?のToString()はSQLへ翻訳できない
            var codes = await logs
                .GroupBy(l => l.ResponseCode)
                .Select(g => new { Code = g.Key, Count = (long)g.Count() })
                .ToListAsync();

            return codes
                .OrderByDescending(c => c.Count)
                .Select(c => new CountStat { Name = c.Code?.ToString() ?? "不明", Count = c.Count })
                .ToList();
        }
    }
}
