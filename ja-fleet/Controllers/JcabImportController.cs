using jafleet.Classes;
using jafleet.Classes.JcabImport;
using jafleet.Commons.EF;
using jafleet.Manager;
using jafleet.Models;
using jafleet.Util;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OfficeOpenXml;

namespace jafleet.Controllers
{
    /// <summary>
    /// 航空局から届くパスワード付きExcelを取り込む画面。
    /// アップロード→解除→解析→プレビューで内容を確認・編集し、選んだ分だけDBに反映する。
    /// </summary>
    public class JcabImportController : Controller
    {
        private readonly JafleetContext _context;
        private readonly IMemoryCache _cache;

        public JcabImportController(JafleetContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        /// <param name="FileName">アップロードされた元のファイル名</param>
        /// <param name="DownloadName">復号版をダウンロードするときのファイル名</param>
        /// <param name="Bytes">復号済みのxlsx</param>
        private sealed record CachedFile(string FileName, string DownloadName, byte[] Bytes);

        public IActionResult Index()
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            JcabImportModel model = new()
            {
                Title = "Excel取込",
                Extension = LoadExtension(),
            };
            return View(model);
        }

        /// <summary>解除して解析し、プレビューを表示する</summary>
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
                return View("Index", model);
            }

            using ExcelPackage package = opened.Package!;
            try
            {
                JcabParseResult parsed = JcabExcelParser.Parse(package);
                model.Preview = new ImportPreviewBuilder(_context).Build(parsed);

                //復号版はダウンロードにも、取込実行時の再解析にも使うのでキャッシュに置いておく
                byte[] decrypted = JcabExcelOpener.ToDecryptedBytes(package);
                model.CacheKey = Guid.NewGuid().ToString("N");
                _cache.Set(CacheKeyOf(model.CacheKey),
                    new CachedFile(model.FileName!, DecryptedFileName(model.FileName), decrypted),
                    JcabImportConstant.CACHE_LIFETIME);
            }
            catch (Exception ex)
            {
                model.ErrorMessage = $"解析に失敗しました。{ex.Message}";
                return View("Index", model);
            }

            LoadMasterLists(model);
            return View("Preview", model);
        }

        /// <summary>
        /// プレビューで選択・編集された内容をDBに反映する。
        /// プレビューの中身はPOSTで往復させず、キャッシュに残した復号済みファイルから解析し直して突き合わせる。
        /// </summary>
        [HttpPost]
        public IActionResult Execute(JcabImportModel model)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            model.Title = "Excel取込";

            if (string.IsNullOrEmpty(model.CacheKey) || _cache.Get(CacheKeyOf(model.CacheKey)) is not CachedFile cached)
            {
                model.ErrorMessage = "アップロードしたファイルの保持期限が切れました。もう一度アップロードしてください。";
                return View("Index", model);
            }

            ExcelOpenResult opened = JcabExcelOpener.OpenDecrypted(cached.Bytes);
            if (!opened.Success)
            {
                model.ErrorMessage = opened.ErrorMessage;
                return View("Index", model);
            }

            using ExcelPackage package = opened.Package!;
            ImportPreview preview = new ImportPreviewBuilder(_context).Build(JcabExcelParser.Parse(package));

            model.FileName = cached.FileName;
            model.Preview = preview;
            model.Result = Apply(model, preview);

            //機体が増えると航空会社別の型式一覧が変わるのでマスタを読み直す
            if (model.Result.CreatedCount > 0)
            {
                MasterManager.ReadAll(_context);
            }

            LoadMasterLists(model);
            return View("Result", model);
        }

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

        private void LoadMasterLists(JcabImportModel model)
        {
            model.AirlineList = MasterManager.AllAirline;
            model.OperationList = MasterManager.Operation;
            model.TypeDetailList = _context.TypeDetails.AsNoTracking().OrderBy(t => t.TypeDetailName).ToArray();
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
                return View("Index", model);
            }

            using ExcelPackage package = opened.Package!;
            byte[] decrypted = JcabExcelOpener.ToDecryptedBytes(package);
            return File(decrypted, JcabImportConstant.XLSX_MIME, DecryptedFileName(model.FileName));
        }

        /// <summary>プレビュー画面から復号版をダウンロードする</summary>
        public IActionResult Download(string id)
        {
            if (!CookieUtil.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(id) || _cache.Get(CacheKeyOf(id)) is not CachedFile cached)
            {
                return NotFound();
            }

            return File(cached.Bytes, JcabImportConstant.XLSX_MIME, cached.DownloadName);
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

        private static string CacheKeyOf(string id) => $"jcabimport:{id}";

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
