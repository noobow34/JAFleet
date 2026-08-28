namespace JAFleet.Classes.JcabImport
{
    public class JcabImportResultRow
    {
        public string RegistrationNumber { get; set; } = string.Empty;

        public bool IsNew { get; set; }

        public bool Success { get; set; }

        /// <summary>失敗理由、または取り込んだ内容</summary>
        public string? Message { get; set; }

        public string? Airline { get; set; }

        public string? TypeDetailName { get; set; }

        public string? OperationName { get; set; }

        public string? RegisterDate { get; set; }

        /// <summary>更新前の内容を履歴に残したか</summary>
        public bool HistoryWritten { get; set; }
    }

    public class JcabImportResult
    {
        public List<JcabImportResultRow> Rows { get; set; } = [];

        public int CreatedCount => Rows.Count(r => r.Success && r.IsNew);

        public int UpdatedCount => Rows.Count(r => r.Success && !r.IsNew);

        public int FailedCount => Rows.Count(r => !r.Success);
    }
}
