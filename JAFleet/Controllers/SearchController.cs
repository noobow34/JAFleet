using Microsoft.AspNetCore.Mvc;
using JAFleet.Classes;
using JAFleet.Models;
using JAFleet.Commons.EF;
using JAFleet.Manager;
using JAFleet.Util;
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
            model.IsAdmin = CookieUtil.IsAdmin(HttpContext);

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
            AircraftView[]? searchResult = null;
            var regList = new List<string?>();
            string[] airline;
            int[] typeDetail = [];
            string[] operation;
            string[] wifi;
            string registrationDate;
            var addSpecialLiveryReg = new List<string>();

            if (model.RegistrationNumber != null)
            {
                //|区切りで複数件を処理
                foreach (string r in model.RegistrationNumber.ToUpper().Split("|"))
                {
                    string reg = string.Empty;
                    if (!r.StartsWith("JA"))
                    {
                        //JAがついていなければ付加
                        reg = "JA";
                    }
                    reg = reg += r.ToUpper();
                    regList.Add(reg);
                }
            }

            //検索
            IQueryable<AircraftView> query = _context.AircraftViews.AsNoTracking();
            if (!string.IsNullOrEmpty(model.Airline))
            {
                airline = model.Airline.Split("|");
                query = query.Where(p => airline.Contains(p.Airline));
            }

            if (!string.IsNullOrEmpty(model.TypeDetail))
            {
                typeDetail = model.TypeDetail.Split("|").Select(t => Convert.ToInt32(t)).ToArray();
                query = query.Where(p => typeDetail.Contains(p.TypeDetailId));
            }

            if (!string.IsNullOrEmpty(model.WiFiCode))
            {
                wifi = model.WiFiCode.Split("|");
                query = query.Where(p => wifi.Contains(p.WifiCode));
            }

            if (!string.IsNullOrEmpty(model.RegistrationDate))
            {
                registrationDate = model.RegistrationDate;
                if (model.RegistrationSelection == "0")
                {
                    query = query.Where(p => p.RegisterDate!.StartsWith(registrationDate));
                }
                else if (model.RegistrationSelection == "1")
                {
                    registrationDate += "zzz";
                    query = query.Where(p => p.RegisterDate!.CompareTo(registrationDate) <= 0);
                }
                else if (model.RegistrationSelection == "2")
                {
                    registrationDate += "   ";
                    query = query.Where(p => p.RegisterDate!.CompareTo(registrationDate) >= 0);
                }
            }

            if (!string.IsNullOrEmpty(model.OperationCode))
            {
                operation = model.OperationCode.Split("|");
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
                addSpecialLiveryReg.AddRange(_context.AircraftHistories.Where(p => !string.IsNullOrEmpty(p.SpecialLivery)).Select(s => s.RegistrationNumber).Distinct().ToList());
                regList.AddRange(addSpecialLiveryReg);
                regList.AddRange(_context.Aircrafts.Where(p => !string.IsNullOrEmpty(p.SpecialLivery)).Select(s => s.RegistrationNumber).Distinct().ToList());
            }
            else if (model.SpecialLivery == "5")
            {
                //キーワード指定（履歴含む）
                //一旦履歴テーブルを検索して、履歴の中の該当のレジを取得
                addSpecialLiveryReg.AddRange(_context.AircraftHistories.Where(p => p.SpecialLivery!.Contains(model.SpecialLiveryKeyword)).Select(s => s.RegistrationNumber).Distinct().ToList());
                regList.AddRange(addSpecialLiveryReg);
                regList.AddRange(_context.Aircrafts.Where(p => p.SpecialLivery!.Contains(model.SpecialLiveryKeyword)).Select(s => s.RegistrationNumber).Distinct().ToList());
            }

            if (regList.Count == 1)
            {
                if (StringUtil.ContainsMetaCharacter(regList[0]!))
                {
                    //メタ文字を含む場合正規表現検索
                    query = query.Where(a => Regex.IsMatch(a.RegistrationNumber, regList[0]!));
                }
                else
                {
                    //含まない場合＝検索
                    query = query.Where(a => a.RegistrationNumber == regList[0]);
                }
            }
            else if (regList.Count > 1)
            {
                //2件以上の場合は正規表現無効でIN検索
                query = query.Where(p => regList.Contains(p.RegistrationNumber));
            }

            try
            {
                searchResult = query.OrderBy(p => p.DisplayOrder).ToArray();
            }
            catch (Npgsql.PostgresException ex)
            {
                if (ex.Message.Contains("invalid regular expression"))
                {
                    return Json(new SearchResult { ErrorMessage = "不正な正規表現が指定されました。正規表現を修正してください。正しい正規表現にもかかわらずこのメッセージが表示される場合は管理人にご連絡ください。" });
                }
            }

            //履歴から検索している場合、その旨追記
            foreach (var reg in addSpecialLiveryReg)
            {
                var addTarget = searchResult?.Where(s => s.RegistrationNumber == reg).SingleOrDefault();
                if (addTarget != null)
                {
                    addTarget.SpecialLivery += "（履歴あり）";
                }
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
            bool isAdmin = CookieUtil.IsAdmin(HttpContext);

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
                    Additional = $"{model.IsDirect},{searchResult?.Length}",
                    UserId = isAdmin.ToString()
                };
                context.Logs.Add(log);

                context.SaveChanges();
            });

            return Json(new SearchResult { ResultList = searchResult!, SearchConditionKey = schash });
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
