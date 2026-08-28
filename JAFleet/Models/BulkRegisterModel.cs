using System.ComponentModel.DataAnnotations;
using JAFleet.Services.BulkRegister;
using JAFleet.Commons.EF;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace JAFleet.Models
{
    /// <summary>
    /// 同一型式の一括登録画面。
    /// レジと登録年月日は1機ずつ違うので1行1機で貼り付け、それ以外の項目は画面上部の共通指定を全機に使う。
    /// </summary>
    public class BulkRegisterModel : BaseModel
    {
        // ---- 全機に共通で入れる項目 ----

        [Display(Name = "航空会社")]
        public string? Airline { get; set; }

        [Display(Name = "詳細型式")]
        public int? TypeDetailId { get; set; }

        [Display(Name = "運用状況")]
        public string? OperationCode { get; set; }

        [Display(Name = "WiFi")]
        public string? WifiCode { get; set; }

        [Display(Name = "コンフィグ")]
        public int? SeatConfig { get; set; }

        [Display(Name = "特別塗装")]
        public string? SpecialLivery { get; set; }

        [Display(Name = "備考")]
        public string? Remarks { get; set; }

        [Display(Name = "整備通知")]
        public bool MaintenanceNotify { get; set; }

        /// <summary>/E の「履歴を作成しない」と同じ。既存機を上書きするときだけ効く。</summary>
        [Display(Name = "履歴を作成しない")]
        public bool NotUpdateDate { get; set; }

        // ---- 1機ずつ指定する項目 ----

        /// <summary>1行1機。「レジ 登録年月日 製造番号」をタブ・カンマ・空白のいずれかで区切る。</summary>
        [Display(Name = "レジと登録年月日")]
        public string? InputText { get; set; }

        /// <summary>プレビューでチェックが入っているレジ。実行対象はここに入っているものだけ。</summary>
        public List<string> SelectedRegs { get; set; } = [];

        // ---- 画面に出すだけのもの ----

        /// <summary>解釈した行。確認を押すと入る。</summary>
        [BindNever]
        public List<BulkRegisterRow>? Rows { get; set; }

        [BindNever]
        public string? ErrorMessage { get; set; }

        [BindNever]
        public string? Message { get; set; }

        [BindNever]
        public List<BulkRegisterResultRow>? Result { get; set; }

        [BindNever]
        public Airline[]? AirlineList { get; set; }

        [BindNever]
        public TypeDetail[]? TypeDetailList { get; set; }

        /// <summary>詳細型式ピッカーの絞り込みと、その場での新規登録に使う</summary>
        [BindNever]
        public Commons.EF.Type[]? TypeList { get; set; }

        [BindNever]
        public Code[]? OperationList { get; set; }

        [BindNever]
        public Code[]? WifiList { get; set; }

        [BindNever]
        public SeatConfiguration[]? SeatConfigurationList { get; set; }
    }

    /// <summary>プレビューの1行。解釈結果にDBの現在値を突き合わせたもの。</summary>
    public class BulkRegisterRow
    {
        public BulkRegisterLine Line { get; set; } = new();

        /// <summary>すでにJAFleetに登録されているレジか</summary>
        public bool Exists { get; set; }

        public string? CurrentAirlineName { get; set; }

        public string? CurrentTypeDetailName { get; set; }

        public string? CurrentOperationName { get; set; }

        public string? CurrentRegisterDate { get; set; }

        /// <summary>取込対象にするか。既存機は既定で外す。</summary>
        public bool Selected { get; set; }
    }

    /// <summary>実行結果の1行</summary>
    public class BulkRegisterResultRow
    {
        public string RegistrationNumber { get; set; } = string.Empty;

        public bool Success { get; set; }

        public bool IsNew { get; set; }

        public bool HistoryWritten { get; set; }

        public string? RegisterDate { get; set; }

        public string? Message { get; set; }
    }
}
