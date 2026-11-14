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

        /// <summary>
        /// 同じグループと見なした行の目印（ImportPreviewBuilder.AssignGroups）。
        /// レジの形・Excelの型式・所有者がすべて同じ行が2つ以上あるときだけ入る。
        /// 画面で、詳細型式とチェックをグループ内の後ろの行へ反映するのに使う。
        /// </summary>
        public string? GroupKey { get; set; }

        /// <summary>画面に出すグループ名。レジの形（JA##EA）。</summary>
        public string? GroupLabel { get; set; }

        /// <summary>グループの機数</summary>
        public int GroupSize { get; set; }

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

            AssignGroups(preview.Items);

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
        /// 同じグループと見なす行に印を付ける。
        ///
        /// 航空会社は同じ機種をまとめて発注して連番のレジ（JA01EA〜JA29EA など）を付けるので、
        /// 1か月分のExcelの中では、レジの形が同じ行はたいてい同じ機材のまとまりになる。
        /// ただしレジの形だけでは粗い。既存機を見ると JA###A は6社18型式、JA###J は4社12型式に
        /// またがっていて、2026年10月分でも JA##XJ に、JALのA350-900の予約20機と、
        /// リース会社へのA350-941の移転（JA14XJ）が混ざっていた。
        /// そこで、Excelに必ず載っている型式と所有者も一致することを条件に加える。
        /// 表記ゆれ（半角カナ・中黒など）はTextUtil.Normalizeで吸収する。
        /// 型式が読めていない行（変更登録だけの行など）は束ねない。
        /// </summary>
        public static void AssignGroups(IEnumerable<ImportPreviewItem> items)
        {
            foreach (IGrouping<string, ImportPreviewItem> group in items
                .Where(i => !string.IsNullOrEmpty(i.TypeDisplay))
                .GroupBy(GroupKeyOf))
            {
                List<ImportPreviewItem> members = group.ToList();
                if (members.Count < 2)
                {
                    continue;
                }

                foreach (ImportPreviewItem member in members)
                {
                    member.GroupKey = group.Key;
                    member.GroupLabel = RegistrationShape(member.RegistrationNumber);
                    member.GroupSize = members.Count;
                }
            }
        }

        public static string GroupKeyOf(ImportPreviewItem item)
            => $"{RegistrationShape(item.RegistrationNumber)}|{TextUtil.Normalize(item.TypeDisplay)}|{TextUtil.Normalize(item.Owner)}";

        /// <summary>
        /// レジの形。数字だけを#にする（JA01EA → JA##EA、JA892J → JA###J）。
        /// 形が同じなら桁数も同じなので、グループ内の前後はレジの文字列比較で決められる。
        /// </summary>
        public static string RegistrationShape(string registration)
            => new(registration.Select(c => char.IsAsciiDigit(c) ? '#' : c).ToArray());

        /// <summary>
        /// 登録年月日の初期値。
        ///   新規登録 … その登録年月日
        ///   予約登録 … F列2行目の納入予定年月日。1行目の予約年月日は予約を受け付けた日で、
        ///              機体の登録年月日ではない。予定は月までのことが多く、そのときは yyyy/MM のまま入れる
        ///   移転・変更・抹消 … JAFleetの現在値を残す。JAFleetに無ければ最新のイベントの日付
        /// 予定年月日が読めなかった予約登録が、最後の逃げ道で予約年月日を拾ってしまわないよう、
        /// 「最新のイベントの日付」の候補から予約登録は外している（空のまま要確認に回す）。
        /// </summary>
        public static string? SuggestRegisterDate(ImportPreviewItem item, Aircraft? aircraft)
        {
            string? fromRegistration = item.Events
                .Where(e => e.RecordType is JcabRecordType.New or JcabRecordType.Reservation)
                .Select(e => e.RecordType == JcabRecordType.Reservation ? e.ScheduledDate : e.RegisterDate)
                .LastOrDefault(d => d != null);

            return fromRegistration
                ?? aircraft?.RegisterDate
                ?? item.Events
                    .Where(e => e.RecordType != JcabRecordType.Reservation)
                    .Select(e => e.RegisterDate)
                    .Where(d => d != null)
                    .Max();
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

            item.EditRegisterDate = SuggestRegisterDate(item, aircraft);

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
                //予約登録の1行目は予約を受け付けた日で、登録年月日ではない
                string label = latest.RecordType == JcabRecordType.Reservation ? "予約年月日" : "登録年月日";
                item.Reasons.Add($"{label}「{latest.RegisterDateRaw}」を日付として読めません");
            }
            //予約登録は2行目の納入予定年月日が登録年月日になる。読めなければ初期値が空になるので確認に回す
            if (latest.RecordType == JcabRecordType.Reservation && latest.ScheduledDate == null)
            {
                item.Reasons.Add(latest.ScheduledDateRaw == null
                    ? "納入予定年月日が空です"
                    : $"納入予定年月日「{latest.ScheduledDateRaw}」を日付として読めません");
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
        /// 所有者名から航空会社を推定する。Phase3でエイリアスマスタに置き換える。
        ///
        /// 所有者名は「日本航空株式会社」「ANAホールディングス株式会社」「株式会社AIRDO」のように、
        /// マスタの名称の前後に会社の種類が付いた形で来る。以前は名称を含んでいれば一致としていたが、
        /// 「中日本航空株式会社」が「日本航空」に当たってJALと推定されていた（2026年10月分のJA848N）。
        /// マスタには ANA・JAL・RAC のような3文字の名称もあり、英語名の所有者（CANADA…）でも誤爆しうる。
        /// そこで会社の種類を前後から外したうえで、名称が**先頭から**一致するものだけを拾う。
        /// 複数当たれば最も長い名称を採用する（リストは長い順に並んでいる）。
        /// </summary>
        public static (string? Code, string? Name) MatchAirline(string? owner, IEnumerable<(string Code, string Name, string Normalized)> airlineNames)
        {
            string core = OwnerCore(owner);
            if (core.Length == 0)
            {
                return (null, null);
            }

            foreach ((string code, string name, string normalized) in airlineNames)
            {
                if (core.StartsWith(normalized, StringComparison.Ordinal))
                {
                    return (code, name);
                }
            }
            return (null, null);
        }

        /// <summary>前後に付く会社の種類。長いものを先に外す必要は無い（どれも他の語を含まない）</summary>
        private static readonly string[] CORPORATE_FORMS =
        [
            "株式会社", "有限会社", "合同会社", "合資会社", "合名会社",
            "一般社団法人", "一般財団法人", "公益社団法人", "公益財団法人",
            "学校法人", "独立行政法人",
        ];

        /// <summary>
        /// 名称の本体。正規化したうえで、前後に付いた会社の種類（株式会社など）を外す。
        /// 所有者名とマスタの名称の両方に通して、同じ土俵で比べる。
        /// </summary>
        public static string OwnerCore(string? name)
        {
            string core = TextUtil.Normalize(name);
            foreach (string form in CORPORATE_FORMS)
            {
                if (core.StartsWith(form, StringComparison.Ordinal))
                {
                    core = core[form.Length..];
                }
                if (core.EndsWith(form, StringComparison.Ordinal))
                {
                    core = core[..^form.Length];
                }
            }
            return core;
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

        private List<(string Code, string Name, string Normalized)> BuildAirlineNames()
            => BuildAirlineNames(MasterManager.AllAirline
                ?? _context.Airlines.AsNoTracking().Where(a => !a.Deleted).ToArray());

        /// <summary>航空会社マスタの各名称を、長いものから順に並べた突き合わせ用リストにする</summary>
        public static List<(string Code, string Name, string Normalized)> BuildAirlineNames(IEnumerable<Airline> airlines)
        {
            List<(string, string, string)> names = [];
            foreach (Airline airline in airlines)
            {
                foreach (string? candidate in new[]
                         {
                             airline.AirlineNameJp, airline.AirlineNameJpShort,
                             airline.AirlineNameEn, airline.AirlineNameEnShort,
                         })
                {
                    //所有者名と同じく会社の種類を外しておく（マスタに「株式会社」付きで入っていても比べられる）
                    string normalized = OwnerCore(candidate);
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
