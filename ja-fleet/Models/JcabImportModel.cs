using System.ComponentModel.DataAnnotations;
using jafleet.Classes.JcabImport;
using jafleet.Commons.EF;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace jafleet.Models
{
    public class JcabImportModel : BaseModel
    {
        [Display(Name = "送信年月日")]
        [DataType(DataType.Date)]
        public DateOnly SendDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);

        [Display(Name = "内線番号")]
        public string? Extension { get; set; }

        [Display(Name = "内線番号を登録し直す")]
        public bool SaveExtension { get; set; }

        /// <summary>自動組み立てが当たらなかったときの逃げ道</summary>
        [Display(Name = "パスワードを直接入力")]
        public string? ManualPassword { get; set; }

        [BindNever]
        public string? ErrorMessage { get; set; }

        [BindNever]
        public ImportPreview? Preview { get; set; }

        /// <summary>
        /// 復号済みファイルのキャッシュキー。ダウンロードにも、取込実行時の再解析にも使う。
        /// プレビューの内容をPOSTで往復させず、キャッシュから解析し直して突き合わせる。
        /// </summary>
        public string? CacheKey { get; set; }

        public string? FileName { get; set; }

        [BindNever]
        public bool WasEncrypted { get; set; }

        /// <summary>実際に解除できた送信年月日</summary>
        [BindNever]
        public DateOnly? MatchedSendDate { get; set; }

        /// <summary>プレビュー画面で編集した行</summary>
        public List<JcabImportRowModel> Rows { get; set; } = [];

        [BindNever]
        public JcabImportResult? Result { get; set; }

        [BindNever]
        public Airline[]? AirlineList { get; set; }

        [BindNever]
        public TypeDetail[]? TypeDetailList { get; set; }

        /// <summary>詳細型式を新規登録するモーダルで選ぶ型式</summary>
        [BindNever]
        public Commons.EF.Type[]? TypeList { get; set; }

        [BindNever]
        public Code[]? OperationList { get; set; }
    }

    /// <summary>プレビュー画面の1行分の編集内容</summary>
    public class JcabImportRowModel
    {
        public string RegistrationNumber { get; set; } = string.Empty;

        public bool Selected { get; set; }

        public string? Airline { get; set; }

        public int? TypeDetailId { get; set; }

        public string? OperationCode { get; set; }

        public string? RegisterDate { get; set; }

        public string? SerialNumber { get; set; }

        public string? Remarks { get; set; }

        /// <summary>/E の「履歴を作成しない」と同じ。チェックすると更新前の内容を履歴に残さず、更新日時も進めない。</summary>
        [Display(Name = "履歴を作成しない")]
        public bool NotUpdateDate { get; set; }
    }
}
