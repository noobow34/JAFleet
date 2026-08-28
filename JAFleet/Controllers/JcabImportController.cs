using JAFleet.Classes;
using JAFleet.Classes.JcabImport;
using JAFleet.Commons.EF;
using JAFleet.Manager;
using JAFleet.Models;
using JAFleet.Util;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;

namespace JAFleet.Controllers
{
    /// <summary>
    /// 航空局から届くパスワード付きExcelを取り込む画面。
    /// アップロード→解除→解析→プレビューで内容を確認・編集し、選んだ分だけDBに反映する。
    /// 解除したファイルと編集内容は一時保存に残るので、途中で離れても再開できる。
    /// </summary>
    public class JcabImportController : Controller
    {
        private readonly JAFleetContext _context;

        public JcabImportController(JAFleetContext context) => _context = context;

        public IActionResult Index([FromQuery] bool fromAdmin)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            JcabImportModel model = new()
            {
                Title = "Excel取込",
                Extension = LoadExtension(),
                FromAdmin = fromAdmin,
                Sessions = LoadSessions(),
            };
            return View(model);
        }

        /// <summary>解除して解析し、一時保存を作ってプレビューを表示する</summary>
        [HttpPost]
        [RequestSizeLimit(30 * 1024 * 1024)]
        public IActionResult Analyze(IFormFile? file, JcabImportModel model)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            model.Title = "Excel取込";
            ExcelOpenResult? opened = OpenUploaded(file, model);
            if (opened == null || !opened.Success)
            {
                model.Sessions = LoadSessions();
                return View("Index", model);
            }

            using ExcelPackage package = opened.Package!;
            try
            {
                JcabParseResult parsed = JcabExcelParser.Parse(package);
                model.Preview = new ImportPreviewBuilder(_context).Build(parsed);

                //解除した時点で一時保存を作る。以降はここのファイルを読み直して作業する。
                JcabImportSession session = new()
                {
                    FileName = model.FileName!,
                    TargetMonth = parsed.TargetMonth?.ToString("yyyy/MM"),
                    FileData = JcabExcelOpener.ToDecryptedBytes(package),
                    OverridesJson = JcabRowOverrideSerializer.Serialize([]),
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                };
                _context.JcabImportSessions.Add(session);
                _context.SaveChanges();

                model.SessionId = session.SessionId;
                model.SavedAt = session.UpdatedAt;
            }
            catch (Exception ex)
            {
                model.ErrorMessage = $"解析に失敗しました。{ex.Message}";
                model.Sessions = LoadSessions();
                return View("Index", model);
            }

            LoadMasterLists(model);
            return View("Preview", model);
        }

        /// <summary>一時保存から作業を再開する</summary>
        public IActionResult Resume(int id, [FromQuery] bool fromAdmin)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            JcabImportModel model = new() { Title = "Excel取込", FromAdmin = fromAdmin, SessionId = id };
            if (!TryLoadPreview(model, out _))
            {
                model.Extension = LoadExtension();
                model.Sessions = LoadSessions();
                return View("Index", model);
            }

            LoadMasterLists(model);
            return View("Preview", model);
        }

        /// <summary>編集内容だけを保存して、プレビューに戻る</summary>
        [HttpPost]
        public IActionResult Save(JcabImportModel model)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            model.Title = "Excel取込";
            if (!TryLoadPreview(model, out JcabImportSession? session, model.Rows))
            {
                model.Extension = LoadExtension();
                model.Sessions = LoadSessions();
                return View("Index", model);
            }

            StoreOverrides(session!, model.Rows);
            model.SavedAt = session!.UpdatedAt;
            model.Message = "編集内容を保存しました。";

            LoadMasterLists(model);
            return View("Preview", model);
        }

        /// <summary>一時保存を削除する</summary>
        [HttpPost]
        public IActionResult DeleteSession(int id, [FromQuery] bool fromAdmin)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            JcabImportSession? session = _context.JcabImportSessions.FirstOrDefault(s => s.SessionId == id);
            if (session != null)
            {
                _context.JcabImportSessions.Remove(session);
                _context.SaveChanges();
            }

            return RedirectToAction("Index", new { fromAdmin });
        }

        /// <summary>
        /// プレビューで選択・編集された内容をDBに反映する。
        /// プレビューの中身はPOSTで往復させず、一時保存のファイルから解析し直して突き合わせる。
        /// </summary>
        [HttpPost]
        public IActionResult Execute(JcabImportModel model)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            model.Title = "Excel取込";
            if (!TryLoadPreview(model, out JcabImportSession? session, model.Rows))
            {
                model.Extension = LoadExtension();
                model.Sessions = LoadSessions();
                return View("Index", model);
            }

            model.Result = Apply(model, model.Preview!);

            //取込できた行に印を付けたうえで編集内容を保存する。再開したときにどこまで済んだか分かる。
            HashSet<string> imported = model.Result.Rows
                .Where(r => r.Success)
                .Select(r => r.RegistrationNumber)
                .ToHashSet();
            StoreOverrides(session!, model.Rows, imported);
            RefreshSessionSummary(model, session!);

            //機体が増えると航空会社別の型式一覧が変わるのでマスタを読み直す
            if (model.Result.CreatedCount > 0)
            {
                MasterManager.ReadAll(_context);
            }

            LoadMasterLists(model);
            return View("Result", model);
        }

        /// <summary>解析せず、解除したファイルをそのまま返す</summary>
        [HttpPost]
        [RequestSizeLimit(30 * 1024 * 1024)]
        public IActionResult Decrypt(IFormFile? file, JcabImportModel model)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            model.Title = "Excel取込";
            ExcelOpenResult? opened = OpenUploaded(file, model);
            if (opened == null || !opened.Success)
            {
                model.Sessions = LoadSessions();
                return View("Index", model);
            }

            using ExcelPackage package = opened.Package!;
            byte[] decrypted = JcabExcelOpener.ToDecryptedBytes(package);
            return File(decrypted, JcabImportConstant.XLSX_MIME, DecryptedFileName(model.FileName));
        }

        /// <summary>一時保存に入っている復号版をダウンロードする</summary>
        public IActionResult Download(int id)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            JcabImportSession? session = _context.JcabImportSessions.AsNoTracking()
                .FirstOrDefault(s => s.SessionId == id);
            if (session == null)
            {
                return NotFound();
            }

            return File(session.FileData, JcabImportConstant.XLSX_MIME, DecryptedFileName(session.FileName));
        }

        /// <summary>
        /// 一時保存のファイルを解析し直してプレビューを組み立てる。
        /// 保存済みの編集内容に、画面から送られてきた内容があればそちらを優先して被せる。
        /// </summary>
        private bool TryLoadPreview(JcabImportModel model, out JcabImportSession? session,
                                    List<JcabImportRowModel>? postedRows = null)
        {
            session = model.SessionId == null
                ? null
                : _context.JcabImportSessions.FirstOrDefault(s => s.SessionId == model.SessionId);

            if (session == null)
            {
                model.ErrorMessage = "一時保存が見つかりませんでした。もう一度アップロードしてください。";
                return false;
            }

            ExcelOpenResult opened = JcabExcelOpener.OpenDecrypted(session.FileData);
            if (!opened.Success)
            {
                model.ErrorMessage = opened.ErrorMessage;
                return false;
            }

            using ExcelPackage package = opened.Package!;
            Dictionary<string, JcabRowOverride> overrides = JcabRowOverrideSerializer.Deserialize(session.OverridesJson);
            if (postedRows != null)
            {
                overrides = MergeOverrides(overrides, postedRows);
            }

            model.FileName = session.FileName;
            model.SavedAt = session.UpdatedAt;
            model.WasEncrypted = true;
            model.Preview = new ImportPreviewBuilder(_context).Build(JcabExcelParser.Parse(package), overrides);
            RefreshSessionSummary(model, session);
            return true;
        }

        /// <summary>取込済みのレジと、そこから作る検索条件の名前をモデルに載せる</summary>
        private static void RefreshSessionSummary(JcabImportModel model, JcabImportSession session)
        {
            model.ImportedRegistrations = JcabRowOverrideSerializer.Deserialize(session.OverridesJson)
                .Where(o => o.Value.Imported)
                .Select(o => o.Key)
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList();
            model.SearchConditionName = BuildSearchConditionName(session.TargetMonth);
        }

        /// <summary>「2026年06月航空機登録動向」の形で名前を作る。target_monthは yyyy/MM。</summary>
        private static string BuildSearchConditionName(string? targetMonth)
        {
            string[] parts = (targetMonth ?? string.Empty).Split('/');
            return parts.Length == 2
                ? $"{parts[0]}年{parts[1]}月航空機登録動向"
                : $"{DateTime.Now:yyyy年MM月}航空機登録動向";
        }

        /// <summary>
        /// この一時保存で取り込んだレジをまとめて名前付き検索条件にする。
        /// 1件ずつ取り込んでも一括で取り込んでも、最後にここを押せば全部が対象になる。
        /// </summary>
        [HttpPost]
        public IActionResult CreateSearchCondition(JcabImportModel model)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            model.Title = "Excel取込";
            if (!TryLoadPreview(model, out JcabImportSession? session))
            {
                model.Extension = LoadExtension();
                model.Sessions = LoadSessions();
                return View("Index", model);
            }

            if (model.ImportedRegistrations.Count == 0)
            {
                model.ErrorMessage = "取り込んだレジがありません。先に取込を実行してください。";
                LoadMasterLists(model);
                return View("Preview", model);
            }

            SearchConditionInModel condition = new()
            {
                //検索側は | 区切りで複数レジを受け取る
                RegistrationNumber = string.Join("|", model.ImportedRegistrations),
            };

            string name = BuildSearchConditionName(session!.TargetMonth);
            model.SearchConditionKey = SearchConditionStore.Save(_context, condition, name);
            model.Message = $"検索条件「{name}」を登録しました（{model.ImportedRegistrations.Count}機）。";

            LoadMasterLists(model);
            return View("Preview", model);
        }

        /// <summary>画面から送られてきた編集内容を保存済みの内容に反映する</summary>
        private static Dictionary<string, JcabRowOverride> MergeOverrides(
            Dictionary<string, JcabRowOverride> overrides, List<JcabImportRowModel> rows,
            HashSet<string>? imported = null)
        {
            foreach (JcabImportRowModel row in rows)
            {
                if (string.IsNullOrEmpty(row.RegistrationNumber))
                {
                    continue;
                }

                overrides.TryGetValue(row.RegistrationNumber, out JcabRowOverride? previous);
                overrides[row.RegistrationNumber] = new JcabRowOverride
                {
                    Selected = row.Selected,
                    Airline = row.Airline,
                    TypeDetailId = row.TypeDetailId,
                    OperationCode = row.OperationCode,
                    RegisterDate = row.RegisterDate,
                    SerialNumber = row.SerialNumber,
                    Remarks = row.Remarks,
                    NotUpdateDate = row.NotUpdateDate,
                    Category = row.Category,
                    Imported = (previous?.Imported ?? false) || (imported?.Contains(row.RegistrationNumber) ?? false),
                };
            }
            return overrides;
        }

        private void StoreOverrides(JcabImportSession session, List<JcabImportRowModel> rows, HashSet<string>? imported = null)
        {
            Dictionary<string, JcabRowOverride> overrides =
                MergeOverrides(JcabRowOverrideSerializer.Deserialize(session.OverridesJson), rows, imported);

            session.OverridesJson = JcabRowOverrideSerializer.Serialize(overrides);
            session.UpdatedAt = DateTime.Now;
            _context.SaveChanges();
        }

        private List<JcabImportSession> LoadSessions()
            => _context.JcabImportSessions.AsNoTracking()
                .OrderByDescending(s => s.UpdatedAt)
                .Select(s => new JcabImportSession
                {
                    //ファイル本体は一覧に要らないので読まない
                    SessionId = s.SessionId,
                    FileName = s.FileName,
                    TargetMonth = s.TargetMonth,
                    CreatedAt = s.CreatedAt,
                    UpdatedAt = s.UpdatedAt,
                })
                .ToList();

        /// <summary>選択された行を機体情報に反映する。1回のSaveChangesでまとめて確定する。</summary>
        private JcabImportResult Apply(JcabImportModel model, ImportPreview preview)
        {
            JcabImportResult result = new();
            DateTime storeDate = DateTime.Now;

            Dictionary<int, string?> typeDetailNames = _context.TypeDetails.AsNoTracking()
                .Where(t => t.TypeDetailId != null)
                .ToDictionary(t => t.TypeDetailId!.Value, t => t.TypeDetailName);

            foreach (JcabImportRowModel row in model.Rows.Where(r => r.Selected))
            {
                ImportPreviewItem? item = preview.Items
                    .FirstOrDefault(i => i.RegistrationNumber == row.RegistrationNumber);

                JcabImportResultRow resultRow = new() { RegistrationNumber = row.RegistrationNumber };
                result.Rows.Add(resultRow);

                if (item == null)
                {
                    resultRow.Message = "解析結果に見つかりませんでした。ファイルが差し替わっている可能性があります。";
                    continue;
                }

                string? error = Validate(row);
                if (error != null)
                {
                    resultRow.Message = error;
                    continue;
                }

                Aircraft? existing = _context.Aircrafts.AsNoTracking()
                    .FirstOrDefault(a => a.RegistrationNumber == row.RegistrationNumber);

                Aircraft aircraft = existing ?? new Aircraft { RegistrationNumber = row.RegistrationNumber };
                aircraft.Airline = row.Airline;
                aircraft.TypeDetailId = row.TypeDetailId!.Value;
                aircraft.OperationCode = row.OperationCode;
                aircraft.RegisterDate = row.RegisterDate;
                aircraft.SerialNumber = row.SerialNumber;
                aircraft.Remarks = row.Remarks;

                bool writeHistory = !row.NotUpdateDate;
                AircraftStore.Store(_context, aircraft, existing == null, writeHistory, storeDate);

                resultRow.IsNew = existing == null;
                resultRow.Success = true;
                resultRow.HistoryWritten = writeHistory && existing != null;
                resultRow.Airline = row.Airline;
                resultRow.TypeDetailName = typeDetailNames.TryGetValue(row.TypeDetailId.Value, out string? name) ? name : null;
                resultRow.OperationName = MasterManager.Operation?.FirstOrDefault(o => o.Key == row.OperationCode)?.Value;
                resultRow.RegisterDate = row.RegisterDate;
            }

            if (result.Rows.Any(r => r.Success))
            {
                _context.SaveChanges();
            }

            return result;
        }

        private static string? Validate(JcabImportRowModel row)
        {
            if (string.IsNullOrEmpty(row.Airline))
            {
                return "航空会社が未選択です。";
            }
            if (row.TypeDetailId is null or 0)
            {
                return "詳細型式が未選択です。";
            }
            if (string.IsNullOrEmpty(row.OperationCode))
            {
                return "運用状況が未選択です。";
            }
            return null;
        }

        /// <summary>アップロードされたファイルを開く。失敗した場合はmodelにエラーを詰めてnullを返す。</summary>
        private ExcelOpenResult? OpenUploaded(IFormFile? file, JcabImportModel model)
        {
            if (file == null || file.Length == 0)
            {
                model.ErrorMessage = "ファイルが選択されていません。";
                return null;
            }

            model.FileName = Path.GetFileName(file.FileName);

            byte[] raw;
            using (MemoryStream ms = new())
            {
                file.CopyTo(ms);
                raw = ms.ToArray();
            }

            if (model.SaveExtension && !string.IsNullOrWhiteSpace(model.Extension))
            {
                StoreExtension(model.Extension.Trim());
            }

            ExcelOpenResult opened = string.IsNullOrWhiteSpace(model.ManualPassword)
                ? JcabExcelOpener.Open(raw, model.SendDate, model.Extension?.Trim(), JcabImportConstant.PASSWORD_RETRY_DAYS)
                : JcabExcelOpener.OpenWithPassword(raw, model.ManualPassword);

            model.WasEncrypted = opened.WasEncrypted;
            model.MatchedSendDate = opened.MatchedSendDate;
            if (!opened.Success)
            {
                model.ErrorMessage = opened.ErrorMessage;
            }

            return opened;
        }

        private void LoadMasterLists(JcabImportModel model)
        {
            model.AirlineList = MasterManager.AllAirline;
            model.OperationList = MasterManager.Operation;
            model.TypeList = MasterManager.Type;
            model.TypeDetailList = _context.TypeDetails.AsNoTracking().OrderBy(t => t.TypeDetailName).ToArray();
        }

        /// <summary>
        /// プレビュー画面のモーダルから詳細型式を新規登録する。
        /// Excelの型式がマスタに無いとき、別画面に移らずその場で追加できるようにするためのもの。
        /// </summary>
        [HttpPost]
        public IActionResult CreateTypeDetail(string? typeCode, string? typeDetailCode, string? typeDetailName)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            TypeDetailStore.Result result = TypeDetailStore.Create(_context, typeCode, typeDetailCode, typeDetailName);
            return result.Error != null
                ? Json(new { error = result.Error })
                : Json(new { id = result.Id, name = result.Name, duplicated = result.Duplicated });
        }

        private static string DecryptedFileName(string? original)
        {
            string stem = string.IsNullOrEmpty(original)
                ? "取込ファイル"
                : Path.GetFileNameWithoutExtension(original);
            return $"{stem}_復号済.xlsx";
        }

        private string? LoadExtension()
            => _context.Codes.AsNoTracking()
                .FirstOrDefault(c => c.CodeType == JcabImportConstant.CODE_TYPE && c.Key == JcabImportConstant.KEY_EXTENSION)
                ?.Value;

        private void StoreExtension(string value)
        {
            Code? code = _context.Codes
                .FirstOrDefault(c => c.CodeType == JcabImportConstant.CODE_TYPE && c.Key == JcabImportConstant.KEY_EXTENSION);

            if (code == null)
            {
                _context.Codes.Add(new Code
                {
                    CodeType = JcabImportConstant.CODE_TYPE,
                    Key = JcabImportConstant.KEY_EXTENSION,
                    Value = value,
                });
            }
            else
            {
                code.Value = value;
            }
            _context.SaveChanges();
        }
    }
}
