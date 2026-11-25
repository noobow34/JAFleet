using Microsoft.AspNetCore.Mvc;
using JAFleet.Services;
using JAFleet.Models;
using JAFleet.Commons.Data;
using JAFleet.Commons.Util;
using JAFleet.Infrastructure;
using JAFleet.Commons.Constants;
using AutoMapper;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Noobow.Commons.Utils;
using Noobow.Commons.Constants;
using EnumStringValues;

namespace JAFleet.Controllers
{
    public class SearchController : Controller
    {

        private readonly JAFleetContext _context;
        private readonly IServiceScopeFactory _services;

        public SearchController(JAFleetContext context, IServiceScopeFactory serviceScopeFactory)
        {
            _context = context;
            _services = serviceScopeFactory;
        }

        /// <summary>
        /// 初期アクション
        /// </summary>
        /// <param name="model"></param>
        /// <param name="sc"></param>
        /// <returns></returns>
        public IActionResult Index(SearchModel model, [FromQuery] string sc)
        {
            if (!string.IsNullOrEmpty(sc))
            {
                //GETパラメーターで検索条件キーが指定されたら
                //保存されている検索条件を取得
                var sce = _context.SearchConditions.AsNoTracking().Where(e => e.SearchConditionKey == sc).FirstOrDefault();
                if (sce != null)
                {
                    //取得したjsonから復元
                    var scm = JsonConvert.DeserializeObject<SearchConditionInModel>(sce.SearchConditionJson!, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                    //modelにコピー
                    ILoggerFactory logger = LoggerFactory.Create(builder => builder.AddDebug());
                    var configuration = new MapperConfiguration(cfg =>
                    {
                        cfg.CreateMap<SearchConditionInModel, SearchModel>();
                    },logger);
                    var mapper = configuration.CreateMapper();
                    model = mapper.Map<SearchModel>(scm);
                    model.IsDirect = true;
                }
            }

            //保存済みの条件から復元するとmodelが作り直されるので、管理者の判定はそのあとで入れる
            model.IsAdmin = AdminAuth.IsAdmin(HttpContext);
            model.AirlineList = MasterManager.AllAirline!;
            model.TypeDetailList = MasterManager.TypeDetailGroup!;
            model.OperationList = MasterManager.Operation!;
            model.WiFiList = MasterManager.Wifi!;

            return View(model);
        }

        /// <summary>
        /// 検索実行
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public IActionResult DoSearch(SearchModel model)
        {
            //modelがnullのリクエストが来る対策
            if (model == null)
            {
                return Json(new SearchResult { ErrorMessage = "検索処理でエラーが発生しました。繰り返しエラーになる場合は管理人にご連絡ください。" });
            }

            if (model.IsLoading && !model.IsDirect)
            {
                //初回ロードかつダイレクト指定ではない場合は空リストを返す
                return Json(new SearchResult { ResultList = [], SearchConditionKey = string.Empty });
            }
            AircraftView[] searchResult;
            try
            {
                searchResult = AircraftSearch.Build(_context, model, out List<string> historyLiveryRegs).ToArray();
                //履歴から検索している場合、その旨追記
                AircraftSearch.MarkHistoryLivery(searchResult, historyLiveryRegs);
            }
            catch (Npgsql.PostgresException ex) when (AircraftSearch.IsInvalidRegex(ex))
            {
                return Json(new SearchResult { ErrorMessage = AircraftSearch.INVALID_REGEX_MESSAGE });
            }

            //検索条件保持用クラスにコピー
            ILoggerFactory logger = LoggerFactory.Create(builder => builder.AddDebug());
            var configuration = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<SearchModel, SearchConditionInModel>();
            }, logger);
            var mapper = configuration.CreateMapper();
            var scm = mapper.Map<SearchConditionInModel>(model);

            //検索条件保持用クラスをJsonにシリアライズ
            string scjson = scm.ToString();
            string schash = HashUtil.CalcCRC32(scjson);
            //Cookieの値はここで退避しておかないと、↓のTask.Runではちゃんと取れなくなる
            bool isAdmin = AdminAuth.IsAdmin(HttpContext);

            //検索結果を速く返すためにログと検索条件のDB書き込みは非同期で行う
            Task.Run(() =>
            {
                using var serviceScope = _services.CreateScope();
                using var context = serviceScope.ServiceProvider.GetService<JAFleetContext>()!;
                //検索条件保存
                var sc = context.SearchConditions.Where(e => e.SearchConditionKey == schash).FirstOrDefault();
                if (sc == null)
                {
                    sc = new SearchCondition
                    {
                        SearchConditionKey = schash,
                        SearchConditionJson = scjson,
                        SearchCount = 0
                    };

                    //検索回数、検索日時は管理者じゃないい場合のみ
                    if (!isAdmin)
                    {
                        sc.SearchCount = 1;
                        sc.FirstSearchDate = DateTime.Now;
                        sc.LastSearchDate = sc.FirstSearchDate;
                    }
                    context.SearchConditions.Add(sc);
                }
                else if (!isAdmin)
                {
                    //管理者じゃない場合のみ検索回数、検索日時を更新
                    sc.SearchCount++;
                    sc.LastSearchDate = DateTime.Now;
                    if (sc.FirstSearchDate == null)
                    {
                        //初回の検索が管理者だった場合に初回検索日時がセットされてないのでここでセット
                        sc.FirstSearchDate = sc.LastSearchDate;
                    }
                }

                //ログ
                Log log = new()
                {
                    LogDate = DateTime.Now,
                    LogType = LogType.SEARCH,
                    LogDetail = schash,
                    Additional = $"{model.IsDirect},{searchResult.Length}",
                    UserId = isAdmin.ToString()
                };
                context.Logs.Add(log);

                context.SaveChanges();
            });

            return Json(new SearchResult { ResultList = searchResult, SearchConditionKey = schash });
        }

        /// <summary>
        /// キーから検索条件をjsonで取得
        /// </summary>
        /// <param name="searchCondition"></param>
        /// <returns></returns>
        public IActionResult GetSearchCondition(string searchCondition)
        {
            if (string.IsNullOrEmpty(searchCondition))
            {
                return BadRequest();
            }

            SearchCondition sc = _context.SearchConditions.Where(sc => sc.SearchConditionKey == searchCondition).Single();
            return Content(sc.SearchConditionJson!, "application/json");
        }

        /// <summary>
        /// 検索条件がすでに登録されいるか、登録されている場合は名称を返す
        /// </summary>
        /// <param name="scm"></param>
        /// <returns></returns>
        public IActionResult ExistsSeachCondition(SearchConditionInModel scm)
        {
            string scjson = scm.ToString();
            string schash = HashUtil.CalcCRC32(scjson);

            var sc = _context.SearchConditions.Where(sc => sc.SearchConditionKey == schash).AsNoTracking().SingleOrDefault();

            return Content(sc?.SearchConditionName!);
        }

        [HttpPost]
        public IActionResult RegisterNamedSearchCondition(SearchConditionInModel scm, string searchConditionName)
        {
            return Content(SearchConditionStore.Save(_context, scm, searchConditionName));
        }
    }
}
