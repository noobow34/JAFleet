using JAFleet.Commons.Data;

namespace JAFleet.Models
{
    /// <summary>
    /// コンフィグのその場登録（_SeatConfigQuickAdd.cshtml）に渡すもの。
    /// 既にある選択肢のとなりに置く部品なので、選択肢そのものは渡さず、
    /// 相手の要素のidだけを受け取ってJSから読み書きする。
    /// </summary>
    public class SeatConfigQuickAddModel
    {
        /// <summary>登録した行を足して選ぶ、コンフィグのselectのid</summary>
        public required string SelectId { get; set; }

        /// <summary>航空会社のselectのid。ここの値がそのまま登録する航空会社になる。</summary>
        public required string AirlineFieldId { get; set; }

        /// <summary>詳細型式の入力のid。選択肢の絞り込みと同じで、ここから型式を引く。</summary>
        public required string TypeDetailFieldId { get; set; }

        /// <summary>詳細型式→型式名の対応を作るためのもの。登録する型式を画面に出すために使う。</summary>
        public TypeDetail[]? TypeDetailList { get; set; }

        public Commons.Data.Type[]? TypeList { get; set; }
    }
}
