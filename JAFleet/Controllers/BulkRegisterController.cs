using JAFleet.Services;
using JAFleet.Services.BulkRegister;
using JAFleet.Commons.Data;
using JAFleet.Models;
using JAFleet.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Controllers
{
    /// <summary>
    /// 同じ型式の機体をまとめて登録する画面。
    /// 航空会社や詳細型式などは1回だけ指定し、1機ずつ違うレジと登録年月日は貼り付けで入れる。
    /// 入力・確認・実行を1画面で行き来する。
    /// </summary>
    public class BulkRegisterController : Controller
    {
        private const string TITLE = "同一型式の一括登録";

        private readonly JAFleetContext _context;

        public BulkRegisterController(JAFleetContext context) => _context = context;

        public IActionResult Index([FromQuery] bool fromAdmin)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            BulkRegisterModel model = new() { Title = TITLE, FromAdmin = fromAdmin };
            LoadMasterLists(model);
            return View(model);
        }

        /// <summary>貼り付けた内容を解釈して、何がどう登録されるかを同じ画面に出す</summary>
        [HttpPost]
        public IActionResult Preview(BulkRegisterModel model)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            model.Title = TITLE;
            BuildRows(model, usePostedSelection: false);
            if (model.Rows!.Count == 0)
            {
                model.ErrorMessage = "レジが1件も読み取れませんでした。";
            }

            LoadMasterLists(model);
            return View("Index", model);
        }

        /// <summary>
        /// 確認した内容をDBに反映する。
        /// 画面から戻ってくるのは入力欄そのものなので、ここでもう一度解釈し直して突き合わせる。
        /// </summary>
        [HttpPost]
        public IActionResult Execute(BulkRegisterModel model)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            model.Title = TITLE;
            BuildRows(model, usePostedSelection: true);
            LoadMasterLists(model);

            string? invalid = ValidateCommon(model);
            if (invalid != null)
            {
                model.ErrorMessage = invalid;
                return View("Index", model);
            }

            List<BulkRegisterRow> targets = model.Rows!.Where(r => r.Selected && r.Line.IsValid).ToList();
            if (targets.Count == 0)
            {
                model.ErrorMessage = "登録する機体が選ばれていません。";
                return View("Index", model);
            }

            model.Result = Apply(model, targets);

            int created = model.Result.Count(r => r.Success && r.IsNew);
            int updated = model.Result.Count(r => r.Success && !r.IsNew);
            int failed = model.Result.Count(r => !r.Success);
            model.Message = $"{created}機を新規登録、{updated}機を更新しました。"
                          + (failed == 0 ? string.Empty : $"　{failed}機は登録できませんでした。");

            //機体が増えると航空会社別の型式一覧が変わるのでマスタを読み直す
            if (created > 0)
            {
                MasterManager.ReadAll(_context);
                LoadMasterLists(model);
            }

            //登録した行を確認欄に残しておいても押し間違えるだけなので、実行後は消す
            model.Rows = null;
            model.InputText = null;

            return View("Index", model);
        }

        /// <summary>選択された行を機体情報に反映する。1回のSaveChangesでまとめて確定する。</summary>
        private List<BulkRegisterResultRow> Apply(BulkRegisterModel model, List<BulkRegisterRow> targets)
        {
            List<BulkRegisterResultRow> result = [];
            DateTime storeDate = DateTime.Now;

            foreach (BulkRegisterRow row in targets)
            {
                string reg = row.Line.RegistrationNumber!;
                BulkRegisterResultRow resultRow = new()
                {
                    RegistrationNumber = reg,
                    RegisterDate = row.Line.RegisterDate,
                };
                result.Add(resultRow);

                Aircraft? existing = _context.Aircrafts.AsNoTracking()
                    .FirstOrDefault(a => a.RegistrationNumber == reg);

                Aircraft aircraft = existing ?? new Aircraft { RegistrationNumber = reg };
                aircraft.Airline = model.Airline;
                aircraft.TypeDetailId = model.TypeDetailId!.Value;
                aircraft.OperationCode = model.OperationCode;
                aircraft.WifiCode = string.IsNullOrEmpty(model.WifiCode) ? null : model.WifiCode;
                aircraft.SeatConfig = model.SeatConfig;
                aircraft.SpecialLivery = Trimmed(model.SpecialLivery);
                aircraft.Remarks = Trimmed(model.Remarks);
                aircraft.MaintenanceNotify = model.MaintenanceNotify;
                aircraft.RegisterDate = row.Line.RegisterDate;
                aircraft.SerialNumber = row.Line.SerialNumber;

                //新規は履歴を作りようがないので、指定が効くのは既存機の上書きのときだけ
                bool writeHistory = !model.NotUpdateDate;
                AircraftStore.Store(_context, aircraft, existing == null, writeHistory, storeDate);

                resultRow.Success = true;
                resultRow.IsNew = existing == null;
                resultRow.HistoryWritten = writeHistory && existing != null;
            }

            try
            {
                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                //まとめて1トランザクションなので、失敗したときは全件が入っていない
                foreach (BulkRegisterResultRow row in result)
                {
                    row.Success = false;
                    row.Message = ex.Message;
                }
            }

            return result;
        }

        /// <summary>入力欄を解釈し、DBの現在値を突き合わせてプレビューの行を作る</summary>
        private void BuildRows(BulkRegisterModel model, bool usePostedSelection)
        {
            List<BulkRegisterLine> lines = BulkRegisterParser.Parse(model.InputText);

            List<string> regs = lines.Where(l => l.IsValid).Select(l => l.RegistrationNumber!).ToList();
            Dictionary<string, Aircraft> existing = _context.Aircrafts.AsNoTracking()
                .Where(a => regs.Contains(a.RegistrationNumber!))
                .ToDictionary(a => a.RegistrationNumber!, a => a);

            Dictionary<int, string?> typeDetailNames = _context.TypeDetails.AsNoTracking()
                .Where(t => t.TypeDetailId != null)
                .ToDictionary(t => t.TypeDetailId!.Value, t => t.TypeDetailName);

            HashSet<string> selected = new(model.SelectedRegs, StringComparer.Ordinal);

            model.Rows = [];
            foreach (BulkRegisterLine line in lines)
            {
                BulkRegisterRow row = new() { Line = line };
                model.Rows.Add(row);

                if (!line.IsValid)
                {
                    continue;
                }

                if (existing.TryGetValue(line.RegistrationNumber!, out Aircraft? current))
                {
                    row.Exists = true;
                    row.CurrentAirlineName = MasterManager.AllAirline?
                        .FirstOrDefault(a => a.AirlineCode == current.Airline)?.AirlineNameJpShort ?? current.Airline;
                    row.CurrentTypeDetailName = typeDetailNames.TryGetValue(current.TypeDetailId, out string? name) ? name : null;
                    row.CurrentOperationName = MasterManager.Operation?
                        .FirstOrDefault(o => o.Key == current.OperationCode)?.Value ?? current.OperationCode;
                    row.CurrentRegisterDate = current.RegisterDate;
                }

                //既存機は上書きになるので、自分でチェックを入れてもらう
                row.Selected = usePostedSelection ? selected.Contains(line.RegistrationNumber!) : !row.Exists;
            }
        }

        private static string? ValidateCommon(BulkRegisterModel model)
        {
            if (string.IsNullOrEmpty(model.Airline))
            {
                return "航空会社を選択してください。";
            }
            if (model.TypeDetailId is null or 0)
            {
                return "詳細型式を選択してください。";
            }
            if (string.IsNullOrEmpty(model.OperationCode))
            {
                return "運用状況を選択してください。";
            }
            return null;
        }

        private static string? Trimmed(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private void LoadMasterLists(BulkRegisterModel model)
        {
            model.AirlineList = MasterManager.AllAirline;
            model.OperationList = MasterManager.Operation;
            model.WifiList = MasterManager.Wifi;
            model.TypeList = MasterManager.Type;
            model.TypeDetailList = _context.TypeDetails.AsNoTracking().OrderBy(t => t.TypeDetailName).ToArray();

            //シートコンフィグは航空会社と型式で絞る。/E と同じ絞り込み。
            string? typeCode = MasterManager.TypeDetailGroup?
                .FirstOrDefault(td => td.TypeDetailId == model.TypeDetailId)?.TypeCode;
            IEnumerable<SeatConfiguration>? q = MasterManager.SeatConfiguration;
            if (!string.IsNullOrEmpty(model.Airline))
            {
                q = q?.Where(sc => sc.Airline == model.Airline);
            }
            if (!string.IsNullOrEmpty(typeCode))
            {
                q = q?.Where(sc => sc.Type == typeCode);
            }
            model.SeatConfigurationList = q?.ToArray();
        }
    }
}
