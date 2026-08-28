using JAFleet.Commons.EF;

namespace JAFleet.Models
{
    /// <summary>
    /// 型式→詳細型式の2段で選ぶピッカー（_TypeDetailPicker.cshtml）に渡すもの。
    /// 詳細型式が増えて1つのセレクトでは探しづらくなったので、型式で絞ってから選ぶ。
    /// </summary>
    public class TypeDetailPickerModel
    {
        /// <summary>値を持つhidden inputのname。既存のモデルバインドに合わせる。</summary>
        public required string FieldName { get; set; }

        /// <summary>hidden inputのid。既存のJSがここを見ているので画面ごとに合わせる。</summary>
        public required string FieldId { get; set; }

        public int? SelectedId { get; set; }

        /// <summary>絞り込みに使う型式</summary>
        public Commons.EF.Type[]? TypeList { get; set; }

        public TypeDetail[]? TypeDetailList { get; set; }
    }
}
