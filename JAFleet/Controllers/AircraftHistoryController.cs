using JAFleet.Commons.Data;
using JAFleet.Commons.Util;
using JAFleet.Infrastructure;
using JAFleet.Models;
using JAFleet.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Controllers
{
    /// <summary>
    /// 更新履歴（aircraft_history）の画面。
    /// 履歴は単票編集や航空局Excel取込のときにAircraftStoreが自動で積むもので、
    /// 間違った内容で積まれてしまったときに直す・消すためだけのもの。
    /// 画面は1つで、一覧がそのまま入力欄になっている。
    /// 表示用の名前はマスタから引く。aircraft_history_viewを使うと、
    /// 詳細型式がマスタに無い行（まさに直したい行）が落ちてしまうため。
    /// </summary>
    public class AircraftHistoryController : Controller
    {
        private const string TITLE = "更新履歴の編集";

        private readonly JAFleetContext _context;

        public AircraftHistoryController(JAFleetContext context) => _context = context;

        public async Task<IActionResult> IndexAsync([FromQuery] string? reg, [FromQuery] bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            return View(await BuildModelAsync(reg, fromAdmin));
        }

        /// <summary>画面に出ている履歴をまとめて保存する。</summary>
        [HttpPost]
        public async Task<IActionResult> SaveAsync(List<AircraftHistoryInput> rows, string reg, bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            AircraftHistoryStore.Result result = AircraftHistoryStore.SaveAll(_context, reg, rows);

            if (result.Error != null)
            {
                //入力し直せるように、保存できなかった内容を画面に残す
                AircraftHistoryModel ng = await BuildModelAsync(reg, fromAdmin);
                ng.Rows = [.. rows];
                ng.ErrorMessage = result.Error;
                ng.ErrorAhId = result.ErrorAhId;
                return View("Index", ng);
            }

            AircraftHistoryModel model = await BuildModelAsync(result.Reg, fromAdmin);
            model.Message = result.Message;
            return View("Index", model);
        }

        /// <summary>
        /// いまの機体情報から履歴を1件作る。
        /// 「履歴を作成しない」のチェックを外し忘れて消えた履歴を作り直すためのリカバリ用。
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SnapshotAsync(string reg, bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            AircraftHistoryStore.Result result = AircraftHistoryStore.Snapshot(_context, reg, DateTime.Now);

            AircraftHistoryModel model = await BuildModelAsync(result.Reg ?? reg, fromAdmin);
            model.Message = result.Message;
            model.ErrorMessage = result.Error;
            return View("Index", model);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAsync(int ahId, bool fromAdmin)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            AircraftHistoryStore.Result result = AircraftHistoryStore.Delete(_context, ahId);

            AircraftHistoryModel model = await BuildModelAsync(result.Reg, fromAdmin);
            model.Message = result.Message;
            model.ErrorMessage = result.Error;
            return View("Index", model);
        }

        private async Task<AircraftHistoryModel> BuildModelAsync(string? reg, bool fromAdmin)
        {
            reg = Normalize(reg);

            AircraftHistoryModel model = new()
            {
                Title = TITLE,
                IsAdmin = true,
                FromAdmin = fromAdmin,
                Reg = reg,
            };

            if (reg == null)
            {
                return model;
            }

            Aircraft? aircraft = await _context.Aircrafts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.RegistrationNumber == reg);
            List<AircraftHistory> histories = await _context.AircraftHistories.AsNoTracking()
                .Where(ah => ah.RegistrationNumber == reg)
                .OrderByDescending(ah => ah.Seq)
                .ToListAsync();

            model.AircraftNotFound = aircraft == null;
            model.Rows = [.. histories.Select(ToInput)];

            //詳細型式は選ぶ側で型式による絞り込みをするので、型式の一覧も要る
            model.AirlineList = MasterManager.AllAirline ?? [];
            model.TypeList = MasterManager.Type ?? [];
            model.TypeDetailList = await _context.TypeDetails.AsNoTracking()
                .OrderBy(t => t.TypeDetailName).ToArrayAsync();
            model.OperationList = MasterManager.Operation ?? [];
            model.WifiList = MasterManager.Wifi ?? [];
            model.SeatConfigurationList = await _context.SeatConfigurations.AsNoTracking()
                .OrderBy(sc => sc.Airline).ThenBy(sc => sc.Type).ToArrayAsync();

            if (aircraft != null)
            {
                model.Current = BuildCurrent(aircraft, model);
            }

            return model;
        }

        private static AircraftHistoryInput ToInput(AircraftHistory history) => new()
        {
            AhId = history.AhId ?? 0,
            Seq = history.Seq,
            HistoryRegisterAt = history.HistoryRegisterAt,
            Airline = history.Airline,
            TypeDetailId = history.TypeDetailId,
            SerialNumber = history.SerialNumber,
            RegisterDate = history.RegisterDate,
            OperationCode = history.OperationCode,
            WifiCode = history.WifiCode,
            SeatConfig = history.SeatConfig,
            SpecialLivery = history.SpecialLivery,
            Remarks = history.Remarks,
            LinkUrl = history.LinkUrl,
            TestRegistration = history.TestRegistration,
            MaintenanceNotify = history.MaintenanceNotify ?? false,
            CreationTime = history.CreationTime,
            UpdateTime = history.UpdateTime,
            ActualUpdateTime = history.ActualUpdateTime,
        };

        /// <summary>いまの機体情報の行。履歴と見比べるために、同じ並びで出す。</summary>
        private static AircraftCurrentRow BuildCurrent(Aircraft aircraft, AircraftHistoryModel model)
        {
            SeatConfiguration? seatConfig = model.SeatConfigurationList
                .FirstOrDefault(sc => sc.ConfigId == aircraft.SeatConfig);

            return new AircraftCurrentRow
            {
                UpdateTime = aircraft.UpdateTime,
                AirlineName = model.AirlineList.FirstOrDefault(a => a.AirlineCode == aircraft.Airline)?.AirlineNameJpShort
                              ?? aircraft.Airline,
                TypeDetailName = model.TypeDetailList.FirstOrDefault(t => t.TypeDetailId == aircraft.TypeDetailId)?.TypeDetailName
                                 ?? $"ID:{aircraft.TypeDetailId}",
                SerialNumber = aircraft.SerialNumber,
                RegisterDate = aircraft.RegisterDate,
                OperationName = model.OperationList.FirstOrDefault(c => c.Key == aircraft.OperationCode)?.Value
                                ?? aircraft.OperationCode,
                WifiName = model.WifiList.FirstOrDefault(c => c.Key == aircraft.WifiCode)?.Value
                           ?? aircraft.WifiCode,
                SeatConfigText = seatConfig == null ? null
                    : SeatConfigUtil.GenerateSeatConfigString(seatConfig.F, seatConfig.C, seatConfig.PY, seatConfig.P, seatConfig.J, seatConfig.Y),
                SpecialLivery = aircraft.SpecialLivery,
                Remarks = aircraft.Remarks,
            };
        }

        /// <summary>レジの表記ゆれを吸収する。管理コンソールと同じくJAは省略できる。</summary>
        private static string? Normalize(string? reg)
        {
            reg = reg?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(reg))
            {
                return null;
            }
            return reg.StartsWith("JA", StringComparison.Ordinal) ? reg : "JA" + reg;
        }
    }
}
