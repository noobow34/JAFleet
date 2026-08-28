using OfficeOpenXml;

namespace JAFleet.Services.JcabImport
{
    public class JcabParseResult
    {
        /// <summary>対象月（各シートのB1セル）</summary>
        public DateOnly? TargetMonth { get; set; }

        public List<JcabRecord> Records { get; set; } = [];

        /// <summary>シートが見つからない等の気付き</summary>
        public List<string> Warnings { get; set; } = [];

        public int CountOf(JcabRecordType type) => Records.Count(r => r.RecordType == type);
    }

    /// <summary>
    /// 航空局Excelのパーサ。
    /// レコードは固定行数ではなく「B列に登録記号がある行から次の登録記号の行の手前まで」の可変長ブロック。
    /// 共有者が多いと1レコードで数十行になる。
    /// </summary>
    public static class JcabExcelParser
    {
        private const int COL_REG = 2;      //B
        private const int COL_TYPE = 3;     //C
        private const int COL_D = 4;
        private const int COL_E = 5;
        private const int COL_F = 6;
        private const int COL_G = 7;
        private const int LAST_SCAN_COL = 8;

        private static readonly Dictionary<JcabRecordType, string> SHEET_PREFIX = new()
        {
            { JcabRecordType.New, "NEW" },
            { JcabRecordType.Delete, "DEL" },
            { JcabRecordType.Transfer, "TRAN" },
            { JcabRecordType.Change, "CNG" },
            { JcabRecordType.Reservation, "RESERVATION" },
            { JcabRecordType.Cancel, "CANCEL" },
        };

        public static JcabParseResult Parse(ExcelPackage package)
        {
            JcabParseResult result = new();

            foreach ((JcabRecordType type, string prefix) in SHEET_PREFIX)
            {
                //シート名は "NEW6.1 " のように月番号と末尾スペースが付くので前方一致で拾う
                ExcelWorksheet? sheet = package.Workbook.Worksheets
                    .FirstOrDefault(w => TextUtil.Normalize(w.Name).StartsWith(prefix, StringComparison.Ordinal));

                if (sheet == null)
                {
                    result.Warnings.Add($"{type.ToJapanese()}のシート（{prefix}…）が見つかりませんでした。");
                    continue;
                }

                if (sheet.Dimension == null)
                {
                    continue;
                }

                result.TargetMonth ??= ToDateOnly(sheet.Cells[1, COL_REG].Value);

                //Excelのヘッダにも件数が書かれているが、シートによって数え方が違い実データと一致しないため使わない
                result.Records.AddRange(type == JcabRecordType.Change
                    ? ParseChangeSheet(sheet)
                    : ParseStandardSheet(sheet, type));
            }

            return result;
        }

        /// <summary>登録記号を起点にブロックを切って読むシート（CNG以外）</summary>
        private static List<JcabRecord> ParseStandardSheet(ExcelWorksheet sheet, JcabRecordType type)
        {
            List<JcabRecord> records = [];
            int headerRow = FindHeaderRow(sheet);
            if (headerRow == 0)
            {
                return records;
            }

            foreach ((int start, int end) in SplitBlocks(sheet, headerRow + 1, COL_REG))
            {
                string? reg = ReadRegistration(sheet, start);
                if (reg == null)
                {
                    continue;
                }

                JcabRecord record = new()
                {
                    RecordType = type,
                    SheetName = sheet.Name.Trim(),
                    Row = start,
                    RegistrationNumber = reg,
                };

                switch (type)
                {
                    case JcabRecordType.New:
                        //B:登録記号 C:型式 D:製造番号 E:定置場 F:所有者 G:登録年月日
                        ReadTypeCells(sheet, start, end, record);
                        record.SerialNumber = Cell(sheet, start, COL_D, end);
                        record.BasePlace = Cell(sheet, start, COL_E, end);
                        ReadOwnerCells(sheet, start, end, COL_F, record);
                        ReadRegisterDate(sheet, start, COL_G, record);
                        break;

                    case JcabRecordType.Delete:
                        //B:登録記号 C:型式 D:所有者 E:原因 F:登録年月日
                        ReadTypeCells(sheet, start, end, record);
                        ReadOwnerCells(sheet, start, end, COL_D, record);
                        record.Reason = JoinColumn(sheet, start, end, COL_E);
                        ReadRegisterDate(sheet, start, COL_F, record);
                        break;

                    case JcabRecordType.Transfer:
                        //B:登録記号 C:型式 D:定置場 E:新所有者 F:旧所有者 G:登録年月日
                        ReadTypeCells(sheet, start, end, record);
                        record.BasePlace = Cell(sheet, start, COL_D, end);
                        ReadOwnerCells(sheet, start, end, COL_E, record);
                        record.PreviousOwner = Cell(sheet, start, COL_F, end);
                        record.PreviousOwnerAddress = Cell(sheet, start + 1, COL_F, end);
                        ReadRegisterDate(sheet, start, COL_G, record);
                        break;

                    case JcabRecordType.Reservation:
                    case JcabRecordType.Cancel:
                        //B:登録記号 C:型式 D:製造番号 E:申請者 F:予約年月日/予定年月日 G:摘要
                        ReadTypeCells(sheet, start, end, record);
                        record.SerialNumber = Cell(sheet, start, COL_D, end);
                        ReadOwnerCells(sheet, start, end, COL_E, record);
                        ReadRegisterDate(sheet, start, COL_F, record);
                        record.ScheduledDate = JcabDateUtil.ToLooseDateString(CellValue(sheet, start + 1, COL_F, end));
                        record.Note = Cell(sheet, start, COL_G, end);
                        break;
                }

                records.Add(record);
            }

            return records;
        }

        /// <summary>
        /// 変更登録シート。1つの登録記号に変更事項が複数ぶら下がることがあり、
        /// 2件目以降はB列が空でC列だけが埋まるので、C列を起点にブロックを切る。
        /// </summary>
        private static List<JcabRecord> ParseChangeSheet(ExcelWorksheet sheet)
        {
            List<JcabRecord> records = [];
            int headerRow = FindHeaderRow(sheet);
            if (headerRow == 0)
            {
                return records;
            }

            int lastRow = FindLastRow(sheet);
            string? currentReg = null;
            //B列の登録記号を先に行番号→レジで拾っておく
            Dictionary<int, string> regByRow = [];
            for (int r = headerRow + 1; r <= lastRow; r++)
            {
                string? reg = ReadRegistration(sheet, r);
                if (reg != null)
                {
                    regByRow[r] = reg;
                }
            }

            foreach ((int start, int end) in SplitBlocks(sheet, headerRow + 1, COL_TYPE))
            {
                for (int r = headerRow + 1; r <= start; r++)
                {
                    if (regByRow.TryGetValue(r, out string? reg))
                    {
                        currentReg = reg;
                    }
                }
                if (currentReg == null)
                {
                    continue;
                }

                JcabRecord record = new()
                {
                    RecordType = JcabRecordType.Change,
                    SheetName = sheet.Name.Trim(),
                    Row = start,
                    RegistrationNumber = currentReg,
                    ChangeItem = Cell(sheet, start, COL_TYPE, end),
                };

                for (int r = start; r <= end; r++)
                {
                    string? value = Cell(sheet, r, COL_D, end);
                    if (value != null)
                    {
                        if (value.StartsWith('新'))
                        {
                            record.ChangeNew = value[1..].Trim();
                        }
                        else if (value.StartsWith('旧'))
                        {
                            record.ChangeOld = value[1..].Trim();
                        }
                        else
                        {
                            //「個人」のように新旧の区別なく1行だけ入るケース
                            record.ChangeNew ??= value;
                        }
                    }

                    if (record.RegisterDate == null)
                    {
                        object? dateValue = CellValue(sheet, r, COL_E, end);
                        if (dateValue != null)
                        {
                            record.RegisterDate = JcabDateUtil.ToDateString(dateValue);
                            record.RegisterDateRaw = TextUtil.Clean(dateValue);
                        }
                    }
                }

                records.Add(record);
            }

            return records;
        }

        private static void ReadTypeCells(ExcelWorksheet sheet, int start, int end, JcabRecord record)
        {
            record.Maker = Cell(sheet, start, COL_TYPE, end);
            record.TypeName = Cell(sheet, start + 1, COL_TYPE, end);
        }

        /// <summary>所有者列は 1行目:名称 2行目:住所 3行目以降:共有者 の並び</summary>
        private static void ReadOwnerCells(ExcelWorksheet sheet, int start, int end, int col, JcabRecord record)
        {
            record.Owner = Cell(sheet, start, col, end);
            record.OwnerAddress = Cell(sheet, start + 1, col, end);
            for (int r = start + 2; r <= end; r++)
            {
                string? value = Cell(sheet, r, col, end);
                if (value != null)
                {
                    record.AdditionalOwners.Add(value);
                }
            }
        }

        private static void ReadRegisterDate(ExcelWorksheet sheet, int start, int col, JcabRecord record)
        {
            object? value = sheet.Cells[start, col].Value;
            record.RegisterDate = JcabDateUtil.ToDateString(value);
            record.RegisterDateRaw = TextUtil.Clean(value);
        }

        /// <summary>B列の値からJA付きの登録記号を作る</summary>
        private static string? ReadRegistration(ExcelWorksheet sheet, int row)
        {
            object? value = sheet.Cells[row, COL_REG].Value;
            if (value == null)
            {
                return null;
            }

            string? raw;
            if (value is double or float or decimal)
            {
                //数字だけのレジは数値として入っていることがある。先頭の0が落ちないよう4桁に戻す
                raw = ((long)Convert.ToDouble(value)).ToString("D4");
            }
            else
            {
                raw = TextUtil.Clean(value);
            }

            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            raw = raw.Normalize(System.Text.NormalizationForm.FormKC).Replace(" ", string.Empty).ToUpperInvariant();
            //ヘッダ行や注記を拾わないよう、レジとして妥当な長さのものだけ通す
            if (raw.Length is < 4 or > 6)
            {
                return null;
            }

            return raw.StartsWith("JA", StringComparison.Ordinal) ? raw : "JA" + raw;
        }

        /// <summary>指定列を起点にブロック（開始行,終了行）を切り出す</summary>
        private static List<(int Start, int End)> SplitBlocks(ExcelWorksheet sheet, int firstDataRow, int keyCol)
        {
            List<(int, int)> blocks = [];
            int lastRow = FindLastRow(sheet);
            int currentStart = 0;

            for (int r = firstDataRow; r <= lastRow; r++)
            {
                if (TextUtil.Clean(sheet.Cells[r, keyCol].Value) != null)
                {
                    if (currentStart != 0)
                    {
                        blocks.Add((currentStart, r - 1));
                    }
                    currentStart = r;
                }
            }

            if (currentStart != 0)
            {
                blocks.Add((currentStart, lastRow));
            }

            return blocks;
        }

        /// <summary>B列が「登録記号」になっている行を探す</summary>
        private static int FindHeaderRow(ExcelWorksheet sheet)
        {
            int limit = Math.Min(12, sheet.Dimension.End.Row);
            for (int r = 1; r <= limit; r++)
            {
                if (TextUtil.Clean(sheet.Cells[r, COL_REG].Value) == "登録記号")
                {
                    return r;
                }
            }
            return 0;
        }

        /// <summary>ExcelのDimensionは実データよりかなり下まで伸びているので、実際に値がある最終行を探す</summary>
        private static int FindLastRow(ExcelWorksheet sheet)
        {
            for (int r = sheet.Dimension.End.Row; r >= 1; r--)
            {
                for (int c = COL_REG; c <= LAST_SCAN_COL; c++)
                {
                    if (TextUtil.Clean(sheet.Cells[r, c].Value) != null)
                    {
                        return r;
                    }
                }
            }
            return 0;
        }

        private static string? Cell(ExcelWorksheet sheet, int row, int col, int maxRow)
            => row > maxRow ? null : TextUtil.Clean(sheet.Cells[row, col].Value);

        private static object? CellValue(ExcelWorksheet sheet, int row, int col, int maxRow)
            => row > maxRow ? null : sheet.Cells[row, col].Value;

        private static string? JoinColumn(ExcelWorksheet sheet, int start, int end, int col)
        {
            List<string> values = [];
            for (int r = start; r <= end; r++)
            {
                string? value = Cell(sheet, r, col, end);
                if (value != null)
                {
                    values.Add(value);
                }
            }
            return values.Count == 0 ? null : string.Join(" ", values);
        }

        private static DateOnly? ToDateOnly(object? value)
        {
            string? date = JcabDateUtil.ToDateString(value);
            return date == null ? null : DateOnly.ParseExact(date, "yyyy/MM/dd");
        }
    }
}
