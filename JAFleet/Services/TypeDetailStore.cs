using JAFleet.Commons.EF;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Services
{
    /// <summary>
    /// 詳細型式の新規登録。マスタに無い型式に出くわしたとき、別画面に移らずその場で追加するためのもの。
    /// Excel取込プレビュー・単票編集（/E）・同一型式の一括登録から同じように呼ぶ。
    /// </summary>
    public static class TypeDetailStore
    {
        /// <summary>画面にそのまま返せる登録結果</summary>
        public sealed class Result
        {
            public int? Id { get; set; }

            public string? Name { get; set; }

            /// <summary>同じ名前が既にあったので作らずにそれを返した</summary>
            public bool Duplicated { get; set; }

            public string? Error { get; set; }
        }

        public static Result Create(JAFleetContext context, string? typeCode, string? typeDetailCode, string? typeDetailName)
        {
            typeCode = typeCode?.Trim();
            typeDetailCode = typeDetailCode?.Trim();
            typeDetailName = typeDetailName?.Trim();

            if (string.IsNullOrEmpty(typeCode))
            {
                return new Result { Error = "型式を選択してください。" };
            }
            if (string.IsNullOrEmpty(typeDetailName))
            {
                return new Result { Error = "詳細型式名を入力してください。" };
            }

            //同じ名前が既にあるなら作らずにそれを返す
            TypeDetail? duplicated = context.TypeDetails.AsNoTracking()
                .FirstOrDefault(t => t.TypeDetailName == typeDetailName);
            if (duplicated != null)
            {
                return new Result { Id = duplicated.TypeDetailId, Name = duplicated.TypeDetailName, Duplicated = true };
            }

            TypeDetail created = new()
            {
                TypeCode = typeCode,
                TypeDetailCode = string.IsNullOrEmpty(typeDetailCode) ? null : typeDetailCode,
                TypeDetailName = typeDetailName,
            };
            context.TypeDetails.Add(created);
            context.SaveChanges();

            try
            {
                MasterManager.ReadAll(context);
            }
            catch (Exception)
            {
                //キャッシュの更新に失敗しても登録自体は成功しているので、画面はそのまま進める
            }

            return new Result { Id = created.TypeDetailId, Name = created.TypeDetailName };
        }
    }
}
