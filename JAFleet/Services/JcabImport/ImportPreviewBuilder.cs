using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Services.JcabImport
{
    public enum ImportCategory
    {
        /// <summary>航空会社や詳細型式が特定できず、人の判断が要るもの</summary>
        NeedsReview,
        /// <summary>そのまま取り込めるもの</summary>
        Ready,
        /// <summary>自動判定はできたが移転登録を含むもの。航空会社を変えるかどうかは人が決める。</summary>
        Transfer,
        /// <summary>JAFleet未登録かつ航空会社に紐づかない、個人所有の小型機など</summary>
        OutOfScope,
    }

    public class ImportPreviewItem
    {
        public string RegistrationNumber { get; set; } = string.Empty;

        /// <summary>同じレジに複数のイベントが来ることがあるので日付順に並べて持つ</summary>
        public List<JcabRecord> Events { get; set; } = [];

        public bool ExistsInJAFleet { get; set; }

        public string? CurrentAirline { get; set; }

        public int? CurrentTypeDetailId { get; set; }

        public string? CurrentTypeDetailName { get; set; }

        public string? CurrentRegisterDate { get; set; }

        public string? CurrentSerialNumber { get; set; }

        public string? CurrentRemarks { get; set; }

        public string? Owner { get; set; }

        public string? TypeDisplay { get; set; }

        /// <summary>Excelの型式2行目。詳細型式の新規登録の初期値に使う。</summary>
        public string? TypeNameRaw { get; set; }

        public string? SuggestedAirlineCode { get; set; }

        public string? SuggestedAirlineName { get; set; }

        public int? SuggestedTypeDetailId { get; set; }

        public string? SuggestedTypeDetailName { get; set; }

        /// <summary>実際に表示するセクション。人がセクションを移動した場合はその行き先。</summary>
        public ImportCategory Category { get; set; }

        /// <summary>自動判定の結果。人が移動したかどうかの判別に使う。</summary>
        public ImportCategory AutoCategory { get; set; }

        public bool MovedByUser => Category != AutoCategory;

        /// <summary>取込対象としてチェックが入っているか</summary>
        public bool Selected { get; set; }

        /// <summary>この一時保存の中で取込済みになったか</summary>
        public bool Imported { get; set; }

        public bool NotUpdateDate { get; set; }

        /// <summary>要確認になった理由</summary>
        public List<string> Reasons { get; set; } = [];

        public string? LatestDate => Events.Select(e => e.RegisterDate).Where(d => d != null).Max();

        //ここから下は取込フォームの初期値。既存機は現在値を、新規はExcelとマスタ推定を入れる。
        public string? EditAirline { get; set; }

        public int? EditTypeDetailId { get; set; }

        public string? EditOperationCode { get; set; }

        public string? EditRegisterDate { get; set; }

        public string? EditSerialNumber { get; set; }

        public string? EditRemarks { get; set; }

        /// <summary>現在の運用状況（表示用）</summary>
        public string? CurrentOperationCode { get; set; }
    }

    public class ImportPreview
    {
        public JcabParseResult Parsed { get; set; } = new();

        public List<ImportPreviewItem> Items { get; set; } = [];

        public IEnumerable<ImportPreviewItem> Of(ImportCategory category)
            => Items.Where(i => i.Category == category);

        public int CountOf(ImportCategory category) => Items.Count(i => i.Category == category);
    }

    /// <summary>
    /// パース結果をレジ単位にまとめ、JAFleetのマスタと突き合わせてプレビュー用に組み立てる。
    /// この段階ではDBを読むだけで一切更新しない。
    /// </summary>
    public class ImportPreviewBuilder
    {
        private readonly JAFleetContext _context;

        public ImportPreviewBuilder(JAFleetContext context) => _context = context;

        /// <param name="overrides">一時保存から復元する編集内容。レジをキーにする。</param>
        public ImportPreview Build(JcabParseResult parsed, Dictionary<string, JcabRowOverride>? overrides = null)
        {
            ImportPreview preview = new() { Parsed = parsed };

            string[] registrations = parsed.Records
                .Select(r => r.RegistrationNumber)
                .Distinct()
                .ToArray();

            Dictionary<string, Aircraft> existing = _context.Aircrafts.AsNoTracking()
                .Where(a => registrations.Contains(a.RegistrationNumber!))
                .ToDictionary(a => a.RegistrationNumber!, a => a);

            List<TypeDetail> typeDetails = _context.TypeDetails.AsNoTracking().ToList();
            Dictionary<int, string?> typeDetailNames = typeDetails
                .Where(t => t.TypeDetailId != null)
                .ToDictionary(t => t.TypeDetailId!.Value, t => t.TypeDetailName);

            List<(string Code, string Name, string Normalized)> airlineNames = BuildAirlineNames();

            foreach (IGrouping<string, JcabRecord> group in parsed.Records.GroupBy(r => r.RegistrationNumber))
            {
                //同じレジが抹消と新規の両方に出ることがあるので日付順に並べる
                List<JcabRecord> events = group
                    .OrderBy(r => r.RegisterDate ?? string.Empty)
                    .ThenBy(r => r.Row)
                    .ToList();

                ImportPreviewItem item = new()
                {
                    RegistrationNumber = group.Key,
                    Events = events,
                };

                existing.TryGetValue(group.Key, out Aircraft? aircraft);
                if (aircraft != null)
                {
                    item.ExistsInJAFleet = true;
                    item.CurrentAirline = aircraft.Airline;
                    item.CurrentOperationCode = aircraft.OperationCode;
                    item.CurrentTypeDetailId = aircraft.TypeDetailId;
                    item.CurrentTypeDetailName = typeDetailNames.TryGetValue(aircraft.TypeDetailId, out string? name) ? name : null;
                    item.CurrentRegisterDate = aircraft.RegisterDate;
                    item.CurrentSerialNumber = aircraft.SerialNumber;
                    item.CurrentRemarks = aircraft.Remarks;
                }

                //最新のイベントが持っている所有者と型式を代表値にする
                JcabRecord latest = events[^1];
                item.Owner = events.Select(e => e.Owner).LastOrDefault(o => !string.IsNullOrEmpty(o));
                item.TypeDisplay = events.Select(e => e.TypeDisplay).LastOrDefault(t => !string.IsNullOrEmpty(t));

                (string? airlineCode, string? airlineName) = MatchAirline(item.Owner, airlineNames);
                item.SuggestedAirlineCode = airlineCode;
                item.SuggestedAirlineName = airlineName;

                string? typeName = events.Select(e => e.TypeName).LastOrDefault(t => !string.IsNullOrEmpty(t));
                item.TypeNameRaw = typeName;
                TypeDetail? typeDetail = MatchTypeDetail(typeName, typeDetails);
                item.SuggestedTypeDetailId = typeDetail?.TypeDetailId;
                item.SuggestedTypeDetailName = typeDetail?.TypeDetailName;

                //分類はフォーム初期値まで決めてから行う（初期値が埋まらない項目が要確認の理由になる）
                BuildEditDefaults(item, aircraft);
                Classify(item, latest);
                item.AutoCategory = item.Category;
                item.Selected = item.Category == ImportCategory.Ready;

                //一時保存から再開した場合は、自動判定の上に人が触った内容を被せる
                if (overrides != null && overrides.TryGetValue(group.Key, out JcabRowOverride? saved))
                {
                    ApplyOverride(item, saved);
                }

                preview.Items.Add(item);
            }

            preview.Items = preview.Items
                .OrderBy(i => (int)i.Category)
                .ThenBy(i => i.RegistrationNumber, StringComparer.Ordinal)
                .ToList();

            return preview;
        }

        /// <summary>保存されていた編集内容を自動判定の上に反映する</summary>
        private static void ApplyOverride(ImportPreviewItem item, JcabRowOverride saved)
        {
            item.Selected = saved.Selected;
            item.Imported = saved.Imported;
            item.NotUpdateDate = saved.NotUpdateDate;

            //nullは「触っていない」を意味するので自動判定の値をそのまま残す
            item.EditAirline = saved.Airline ?? item.EditAirline;
            item.EditTypeDetailId = saved.TypeDetailId ?? item.EditTypeDetailId;
            item.EditOperationCode = saved.OperationCode ?? item.EditOperationCode;
            item.EditRegisterDate = saved.RegisterDate ?? item.EditRegisterDate;
            item.EditSerialNumber = saved.SerialNumber ?? item.EditSerialNumber;
            item.EditRemarks = saved.Remarks ?? item.EditRemarks;

            if (saved.Category != null)
            {
                item.Category = saved.Category.Value;
            }
        }

        /// <summary>
        /// 画面に出す編集フォームの初期値を決める。
        /// 既存機は現在値を残し、Excelから読めたものだけ上書き候補にする。
        /// 初期値をそのまま実行しても現状維持になるので、意図しない書き換えが起きない。
        /// </summary>
        private static void BuildEditDefaults(ImportPreviewItem item, Aircraft? aircraft)
        {
            item.EditAirline = aircraft?.Airline ?? item.SuggestedAirlineCode;

            item.EditTypeDetailId = aircraft is { TypeDetailId: > 0 }
                ? aircraft.TypeDetailId
                : item.SuggestedTypeDetailId;

            item.EditOperationCode = SuggestOperationCode(item, aircraft);

            //新規登録・予約登録はその日付が登録年月日そのもの。移転や変更では現在値を残す。
            JcabRecord? registerEvent = item.Events.LastOrDefault(e =>
                e.RecordType is JcabRecordType.New or JcabRecordType.Reservation && e.RegisterDate != null);
            item.EditRegisterDate = registerEvent?.RegisterDate
                ?? aircraft?.RegisterDate
                ?? item.LatestDate;

            //製造番号は「未定」で来ることがあるので採用しない
            string? serial = item.Events
                .Select(e => e.SerialNumber)
                .LastOrDefault(s => !string.IsNullOrEmpty(s) && s != "未定");
            item.EditSerialNumber = serial ?? aircraft?.SerialNumber;

            //抹消されたレジは備考に抹消年月日を残す。それ以外はJAFleet側で自由に使っている項目なので触らない。
            JcabRecord last = item.Events[^1];
            item.EditRemarks = last is { RecordType: JcabRecordType.Delete, RegisterDate: not null }
                ? $"{last.RegisterDate}抹消"
                : aircraft?.Remarks;
        }

        private static string? SuggestOperationCode(ImportPreviewItem item, Aircraft? aircraft)
        {
            //同じ月に抹消と新規の両方が来ることがあるため、最後のイベントで最終状態を判断する
            JcabRecord last = item.Events[^1];

            if (last.RecordType == JcabRecordType.Delete)
            {
                return OperationCode.RETIRE_UNREGISTERED;
            }
            if (aircraft?.OperationCode != null)
            {
                return aircraft.OperationCode;
            }
            //予約取り下げは何が正しいか一意に決まらないので選ばせる
            return last.RecordType switch
            {
                JcabRecordType.New => OperationCode.DELIVERY,
                JcabRecordType.Reservation => OperationCode.RESERVED,
                _ => null,
            };
        }

        private static void Classify(ImportPreviewItem item, JcabRecord latest)
        {
            //JAFleetに無く、所有者も航空会社に紐づかないものは対象外候補（個人所有の小型機など）
            if (!item.ExistsInJAFleet && item.SuggestedAirlineCode == null)
            {
                item.Reasons.Add("JAFleet未登録で、所有者が航空会社マスタに紐づきません");
                item.Category = ImportCategory.OutOfScope;
                return;
            }

            //変更登録は商号・住所・定置場の変更で、JAFleet側に反映する項目がない。
            //移転登録などを伴わず変更登録だけのレジは取込対象から外す。
            if (item.Events.All(e => e.RecordType == JcabRecordType.Change))
            {
                string items = string.Join("・", item.Events
                    .Select(e => e.ChangeItem)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct());
                item.Reasons.Add($"変更登録（{items}）のみのため、JAFleetで更新する項目がありません");
                item.Category = ImportCategory.OutOfScope;
                return;
            }

            //所有者が変わりうるイベントで新しい所有者を特定できないなら、既存の航空会社のままでよいか確認したい
            bool ownerChanged = item.Events.Any(e =>
                e.RecordType is JcabRecordType.New or JcabRecordType.Transfer or JcabRecordType.Reservation);
            if (ownerChanged && item.SuggestedAirlineCode == null)
            {
                item.Reasons.Add("航空会社を特定できません");
            }

            //以下は初期値が埋まらない項目。このまま実行すると弾かれるので先に要確認へ落とす
            if (string.IsNullOrEmpty(item.EditAirline))
            {
                item.Reasons.Add("航空会社が未設定です");
            }
            if (item.EditTypeDetailId is null or 0)
            {
                item.Reasons.Add(string.IsNullOrEmpty(item.TypeDisplay)
                    ? "詳細型式が未設定です"
                    : $"詳細型式「{item.TypeDisplay}」がマスタにありません");
            }
            if (string.IsNullOrEmpty(item.EditOperationCode))
            {
                item.Reasons.Add("運用状況を特定できません");
            }
            if (latest.RegisterDate == null && latest.RegisterDateRaw != null)
            {
                item.Reasons.Add($"登録年月日「{latest.RegisterDateRaw}」を日付として読めません");
            }

            if (item.Reasons.Count > 0)
            {
                item.Category = ImportCategory.NeedsReview;
                return;
            }

            //移転登録は所有者が変わっただけで運航会社は変わらないこともあるので、判定できていても分けて出す
            item.Category = item.Events.Any(e => e.RecordType == JcabRecordType.Transfer)
                ? ImportCategory.Transfer
                : ImportCategory.Ready;
        }

        /// <summary>
        /// 所有者名から航空会社を推定する。
        /// 「ANAホールディングス株式会社」のようにマスタの名称を含む形で入ってくるので部分一致で拾い、
        /// 一致した名称が最も長いものを採用する。Phase3でエイリアスマスタに置き換える。
        /// </summary>
        private static (string? Code, string? Name) MatchAirline(string? owner, List<(string Code, string Name, string Normalized)> airlineNames)
        {
            if (string.IsNullOrEmpty(owner))
            {
                return (null, null);
            }

            string normalizedOwner = TextUtil.Normalize(owner);
            foreach ((string code, string name, string normalized) in airlineNames)
            {
                if (normalizedOwner.Contains(normalized, StringComparison.Ordinal))
                {
                    return (code, name);
                }
            }
            return (null, null);
        }

        private static TypeDetail? MatchTypeDetail(string? typeName, List<TypeDetail> typeDetails)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            string normalized = TextUtil.Normalize(typeName);
            return typeDetails.FirstOrDefault(t =>
                       TextUtil.Normalize(t.TypeDetailCode) == normalized)
                ?? typeDetails.FirstOrDefault(t =>
                       TextUtil.Normalize(t.TypeDetailName) == normalized);
        }

        /// <summary>航空会社マスタの各名称を、長いものから順に並べた突き合わせ用リストにする</summary>
        private List<(string Code, string Name, string Normalized)> BuildAirlineNames()
        {
            IEnumerable<Airline> airlines = MasterManager.AllAirline
                ?? _context.Airlines.AsNoTracking().Where(a => !a.Deleted).ToArray();

            List<(string, string, string)> names = [];
            foreach (Airline airline in airlines)
            {
                foreach (string? candidate in new[]
                         {
                             airline.AirlineNameJp, airline.AirlineNameJpShort,
                             airline.AirlineNameEn, airline.AirlineNameEnShort,
                         })
                {
                    string normalized = TextUtil.Normalize(candidate);
                    //1文字の名称は誤爆するので使わない
                    if (normalized.Length >= 2)
                    {
                        names.Add((airline.AirlineCode, candidate!, normalized));
                    }
                }
            }

            return names
                .GroupBy(n => n.Item3)
                .Select(g => g.First())
                .OrderByDescending(n => n.Item3.Length)
                .ToList();
        }
    }
}
