using System.Security;
using OfficeOpenXml;

namespace JAFleet.Services.JcabImport
{
    public class ExcelOpenResult
    {
        public bool Success { get; set; }

        /// <summary>元ファイルが暗号化されていたか</summary>
        public bool WasEncrypted { get; set; }

        public ExcelPackage? Package { get; set; }

        /// <summary>実際に解除できた送信年月日</summary>
        public DateOnly? MatchedSendDate { get; set; }

        /// <summary>試した送信年月日（画面に出して手入力の手がかりにする）</summary>
        public List<DateOnly> TriedSendDates { get; set; } = [];

        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// パスワード付きExcelの解除とオープンを担当する。
    /// パスワードは 送信年月日(yyyyMMdd) + "_" + 内線番号 で組み立てる。
    /// 受け取ってから何日か寝かせてアップロードされることがあるので、当日から数日分を自動で試す。
    /// </summary>
    public static class JcabExcelOpener
    {
        /// <summary>複合ファイル（CFB）のマジックナンバー。暗号化されたxlsxはこれで始まる。</summary>
        private static readonly byte[] CFB_MAGIC = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

        public static string BuildPassword(DateOnly sendDate, string extension)
            => $"{sendDate:yyyyMMdd}_{extension}";

        public static bool IsEncrypted(byte[] raw)
            => raw.Length >= CFB_MAGIC.Length && raw.Take(CFB_MAGIC.Length).SequenceEqual(CFB_MAGIC);

        /// <summary>送信年月日と内線番号からパスワードを組み立てて開く。失敗したら日付を遡って再試行する。</summary>
        public static ExcelOpenResult Open(byte[] raw, DateOnly sendDate, string? extension, int retryDays)
        {
            ExcelOpenResult result = new() { WasEncrypted = IsEncrypted(raw) };

            if (!result.WasEncrypted)
            {
                //手元で解除済みのファイルはそのまま開く
                return OpenPlain(raw, result);
            }

            if (string.IsNullOrWhiteSpace(extension))
            {
                result.ErrorMessage = "内線番号が登録されていません。内線番号を入力してください。";
                return result;
            }

            Exception? lastException = null;
            for (int i = 0; i <= retryDays; i++)
            {
                DateOnly tryDate = sendDate.AddDays(-i);
                result.TriedSendDates.Add(tryDate);
                try
                {
                    result.Package = new ExcelPackage(ToReadWriteStream(raw), BuildPassword(tryDate, extension));
                    result.MatchedSendDate = tryDate;
                    result.Success = true;
                    return result;
                }
                catch (SecurityException ex)
                {
                    //パスワード違い。次の日付へ
                    lastException = ex;
                }
                catch (Exception ex)
                {
                    //パスワード以外の理由で開けない場合は日付を変えても無駄なので打ち切る
                    result.ErrorMessage = $"ファイルを開けませんでした。{ex.Message}";
                    return result;
                }
            }

            string tried = string.Join(" / ", result.TriedSendDates.Select(d => d.ToString("yyyyMMdd")));
            result.ErrorMessage = $"パスワードが違います。{tried} の{result.TriedSendDates.Count}通りを試しました。"
                                + $"送信年月日か内線番号を確認するか、パスワードを直接入力してください。（{lastException?.Message}）";
            return result;
        }

        /// <summary>パスワードを直接指定して開く。自動組み立てが当たらなかったときの逃げ道。</summary>
        public static ExcelOpenResult OpenWithPassword(byte[] raw, string password)
        {
            ExcelOpenResult result = new() { WasEncrypted = IsEncrypted(raw) };

            if (!result.WasEncrypted)
            {
                return OpenPlain(raw, result);
            }

            try
            {
                result.Package = new ExcelPackage(ToReadWriteStream(raw), password);
                result.Success = true;
            }
            catch (SecurityException)
            {
                result.ErrorMessage = "パスワードが違います。";
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"ファイルを開けませんでした。{ex.Message}";
            }
            return result;
        }

        /// <summary>暗号化されていないxlsxを開く。キャッシュに残した復号済みファイルを読み直すときに使う。</summary>
        public static ExcelOpenResult OpenDecrypted(byte[] raw)
            => OpenPlain(raw, new ExcelOpenResult { WasEncrypted = IsEncrypted(raw) });

        /// <summary>
        /// 復号済みのxlsxのバイト列を作る。EPPlusが再シリアライズするため原本とバイト一致はしない。
        /// 暗号化情報を明示的に落としてから書き出さないと、同じパスワードで再暗号化されてしまう。
        /// </summary>
        public static byte[] ToDecryptedBytes(ExcelPackage package)
        {
            package.Encryption.IsEncrypted = false;
            package.Encryption.Password = null;
            return package.GetAsByteArray();
        }

        private static ExcelOpenResult OpenPlain(byte[] raw, ExcelOpenResult result)
        {
            try
            {
                result.Package = new ExcelPackage(ToReadWriteStream(raw));
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Excelとして読み込めませんでした。{ex.Message}";
            }
            return result;
        }

        /// <summary>EPPlusは読み書き可能なストリームしか受け付けないので必ずMemoryStreamに写す。</summary>
        private static MemoryStream ToReadWriteStream(byte[] raw)
        {
            MemoryStream ms = new();
            ms.Write(raw, 0, raw.Length);
            ms.Position = 0;
            return ms;
        }
    }
}
