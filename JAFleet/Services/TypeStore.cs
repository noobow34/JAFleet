using JAFleet.Commons.Data;
using Microsoft.EntityFrameworkCore;
using Type = JAFleet.Commons.Data.Type;

namespace JAFleet.Services
{
    /// <summary>
    /// 型式（type）のその場登録。詳細型式を足そうとしたら親の型式ごとマスタに無い、
    /// というときに別画面へ移らずに追加するためのもの。<see cref="TypeDetailStore"/> の1階層上で、
    /// 詳細型式の選択モーダル（_TypeDetailPicker）とExcel取込プレビューから同じように呼ぶ。
    /// </summary>
    public static class TypeStore
    {
        /// <summary>型式コードの最大長。`/Aircraft/Type/{型式コード}` のURLにも出るので短く抑える。</summary>
        public const int MAX_CODE_LENGTH = 20;

        public const int MAX_NAME_LENGTH = 50;

        public const int MAX_DISPLAY_ORDER_LENGTH = 10;

        /// <summary>画面にそのまま返せる登録結果</summary>
        public sealed class Result
        {
            /// <summary>型式コード。型式の主キーなので、詳細型式と違いIDではなくこれを返す</summary>
            public string? Code { get; set; }

            /// <summary>選択肢に出す表示名（型式名）</summary>
            public string? Name { get; set; }

            /// <summary>同じ型式コードが既にあったので作らずにそれを返した</summary>
            public bool Duplicated { get; set; }

            public string? Error { get; set; }
        }

        /// <summary>
        /// DBを見ないで分かる範囲の検査。
        /// 型式コードは主キーで、かつ `/Aircraft/Type/{型式コード}` のURLに入るため記号を通さない。
        /// </summary>
        public static string? Validate(string? typeCode, string? typeName, string? displayOrder)
        {
            string code = typeCode?.Trim() ?? string.Empty;
            string name = typeName?.Trim() ?? string.Empty;
            string order = displayOrder?.Trim() ?? string.Empty;

            if (code.Length == 0)
            {
                return "型式コードを入力してください。";
            }
            if (code.Length > MAX_CODE_LENGTH)
            {
                return $"型式コードが長すぎます。{MAX_CODE_LENGTH}文字までです。";
            }
            if (!code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
            {
                return "型式コードは半角英数字と - _ . だけで入力してください。";
            }
            if (name.Length == 0)
            {
                return "型式名を入力してください。";
            }
            if (name.Length > MAX_NAME_LENGTH)
            {
                return $"型式名が長すぎます。{MAX_NAME_LENGTH}文字までです。";
            }
            if (order.Length > MAX_DISPLAY_ORDER_LENGTH)
            {
                return $"表示順が長すぎます。{MAX_DISPLAY_ORDER_LENGTH}文字までです。";
            }

            return null;
        }

        /// <summary>
        /// 型式を1件登録する。
        /// 同じ型式コードが既にあってもエラーにせずそれを返す
        /// （欲しいのは「この型式の行」であって、作ること自体ではないため。詳細型式のその場登録と同じ考え方）。
        /// </summary>
        public static Result Create(JAFleetContext context, string? typeCode, string? typeName, string? makerCode, string? displayOrder)
        {
            string? invalid = Validate(typeCode, typeName, displayOrder);
            if (invalid != null)
            {
                return new Result { Error = invalid };
            }

            string code = typeCode!.Trim();
            string name = typeName!.Trim();
            string? maker = string.IsNullOrEmpty(makerCode?.Trim()) ? null : makerCode!.Trim();
            //空欄のままにしておくと一覧の最後に並ぶ（PostgreSQLの昇順はNULLが後）
            string? order = string.IsNullOrEmpty(displayOrder?.Trim()) ? null : displayOrder!.Trim();

            //型式コードは主キー。同じものが既にあるなら作らずにそれを返す
            Type? duplicated = context.Types.AsNoTracking().FirstOrDefault(t => t.TypeCode == code);
            if (duplicated != null)
            {
                return new Result
                {
                    Code = duplicated.TypeCode,
                    Name = string.IsNullOrEmpty(duplicated.TypeName) ? duplicated.TypeCode : duplicated.TypeName,
                    Duplicated = true,
                };
            }

            if (maker != null && !context.Makers.AsNoTracking().Any(m => m.MakerCode == maker))
            {
                return new Result { Error = $"メーカー {maker} がマスタにありません。" };
            }

            Type created = new()
            {
                TypeCode = code,
                TypeName = name,
                MakerCode = maker,
                DisplayOrder = order,
            };
            context.Types.Add(created);
            context.SaveChanges();

            try
            {
                MasterManager.ReadAll(context);
            }
            catch (Exception)
            {
                //キャッシュの更新に失敗しても登録自体は成功しているので、画面はそのまま進める
            }

            return new Result { Code = created.TypeCode, Name = created.TypeName };
        }
    }
}
