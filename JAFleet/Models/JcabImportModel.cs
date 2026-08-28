using System.ComponentModel.DataAnnotations;
using JAFleet.Classes.JcabImport;
using JAFleet.Commons.EF;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace JAFleet.Models
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

        [BindNever]
        public string? Message { get; set; }

        /// <summary>この一時保存で実際に取り込んだレジ。検索条件の対象になる。</summary>
        [BindNever]
        public List<string> ImportedRegistrations { get; set; } = [];

        [BindNever]
        public string? SearchConditionName { get; set; }

        /// <summary>登録した検索条件のキー。登録直後だけ入る。</summary>
        [BindNever]
        public string? SearchConditionKey { get; set; }

        public string? FileName { get; set; }

        [BindNever]
        public bool WasEncrypted { get; set; }

        /// <summary>実際に解除できた送信年月日</summary>
        [BindNever]
        public DateOnly? MatchedSendDate { get; set; }

        /// <summary>プレビュー画面で編集した行</summary>
        public List<JcabImportRowModel> Rows { get; set; } = [];

        /// <summary>一時保存のID。プレビューを開いている間ずっと持ち回る。</summary>
        public int? SessionId { get; set; }

        [BindNever]
        public DateTime? SavedAt { get; set; }

        /// <summary>再開できる一時保存の一覧（アップロード画面用）</summary>
        [BindNever]
        public List<JcabImportSession>? Sessions { get; set; }

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

        /// <summary>人がセクションを移動した場合の行き先</summary>
        public ImportCategory? Category { get; set; }
    }
}
