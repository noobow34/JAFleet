using AutoMapper;
using jafleet.Commons.Constants;
using jafleet.Commons.EF;
using Microsoft.EntityFrameworkCore;

namespace jafleet.Classes
{
    /// <summary>
    /// 機体情報の登録・更新。単票編集（/E）とExcel一括取込（/JcabImport）の両方から使う。
    /// SaveChangesは呼び出し側で行うので、一括取込は1トランザクションにまとめられる。
    /// </summary>
    public static class AircraftStore
    {
        /// <summary>
        /// 登録・更新の内容をコンテキストに積む。
        /// writeHistoryがtrueのときだけ更新前の内容をaircraft_historyに退避し、更新日時を進める。
        /// </summary>
        public static void Store(JafleetContext context, Aircraft aircraft, bool isNew, bool writeHistory, DateTime storeDate)
        {
            if (writeHistory || isNew)
            {
                aircraft.UpdateTime = storeDate;
            }
            aircraft.ActualUpdateTime = storeDate;

            if (isNew)
            {
                aircraft.CreationTime = storeDate;
                context.Aircrafts.Add(aircraft);
            }
            else
            {
                if (writeHistory)
                {
                    AddHistory(context, aircraft.RegistrationNumber, storeDate);
                }
                context.Entry(aircraft).State = EntityState.Modified;
            }

            //デリバリーされたらテストレジはクリア
            if (!OperationCode.PRE_DELIVERY.Contains(aircraft.OperationCode))
            {
                aircraft.TestRegistration = null;
            }
        }

        /// <summary>更新前の内容をaircraft_historyにコピーする</summary>
        private static void AddHistory(JafleetContext context, string? registrationNumber, DateTime storeDate)
        {
            Aircraft? origin = context.Aircrafts.AsNoTracking()
                .FirstOrDefault(a => a.RegistrationNumber == registrationNumber);
            if (origin == null)
            {
                return;
            }

            ILoggerFactory logger = LoggerFactory.Create(builder => builder.AddDebug());
            var configuration = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Aircraft, AircraftHistory>();
            }, logger);
            AircraftHistory history = configuration.CreateMapper().Map<AircraftHistory>(origin);
            history.HistoryRegisterAt = storeDate;

            //HistoryのSEQのMAXを取得
            var maxseq = context.AircraftHistories.AsNoTracking()
                .Where(ah => ah.RegistrationNumber == history.RegistrationNumber)
                .GroupBy(ah => ah.RegistrationNumber)
                .Select(ah => new { maxseq = ah.Max(x => x.Seq) })
                .FirstOrDefault();
            history.Seq = (maxseq?.maxseq ?? 0) + 1;

            context.AircraftHistories.Add(history);
        }
    }
}
