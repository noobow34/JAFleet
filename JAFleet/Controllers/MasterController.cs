using JAFleet.Services;
using JAFleet.Commons.Data;
using JAFleet.Infrastructure;
using JAFleet.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Controllers
{
    public class MasterController : Controller
    {
        private readonly JAFleetContext _context;

        public MasterController(JAFleetContext context) => _context = context;

        public IActionResult AirlineType(string id)
        {
            if (id == null)
            {
                return Json(MasterManager.AirlineType?.Values);
            }
            return Json(MasterManager.AirlineType?[id]);
        }

        public IActionResult NamedSearchCondition()
        {
            return Json(MasterManager.NamedSearchCondition);
        }

        public IActionResult SeatConfiguration(string airline, int typeDetailId)
        {
            var type = MasterManager.TypeDetailGroup?.Where(td => td.TypeDetailId == typeDetailId).FirstOrDefault()?.TypeCode;
            IEnumerable<SeatConfiguration>? q = MasterManager.SeatConfiguration;
            if (!string.IsNullOrEmpty(airline))
            {
                q = q?.Where(sc => sc.Airline == airline);
            }
            if (!string.IsNullOrEmpty(type))
            {
                q = q?.Where(sc => sc.Type == type);
            }

            return Json(q?.ToArray());
        }

        public IActionResult GetAllReg()
        {
            return Json(_context.Aircrafts.AsNoTracking().Select(a => a.RegistrationNumber!.Substring(2)).ToArray());
        }

        /// <summary>
        /// 詳細型式の選択モーダルから新規登録する。
        /// マスタに無い型式に出くわしたとき、編集中の画面を離れずに追加できるようにするためのもの。
        /// </summary>
        [HttpPost]
        public IActionResult CreateTypeDetail(string? typeCode, string? typeDetailCode, string? typeDetailName)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            TypeDetailStore.Result result = TypeDetailStore.Create(_context, typeCode, typeDetailCode, typeDetailName);
            return result.Error != null
                ? Json(new { error = result.Error })
                : Json(new { id = result.Id, name = result.Name, duplicated = result.Duplicated });
        }

        /// <summary>
        /// コンフィグ（座席仕様）をその場で登録する。単票編集（/E）のコンフィグ欄から呼ぶ。
        /// 型式は選択肢の絞り込みと同じ理屈で、選ばれている詳細型式から引く。
        /// </summary>
        [HttpPost]
        public IActionResult CreateSeatConfig(string? airline, int typeDetailId, int? f, int? c, int? py, int? p, int? j, int? y, string? url)
        {
            if (!AdminAuth.IsAdmin(HttpContext))
            {
                return NotFound();
            }

            string? typeCode = MasterManager.TypeDetailGroup?
                .FirstOrDefault(td => td.TypeDetailId == typeDetailId)?.TypeCode;
            if (string.IsNullOrEmpty(typeCode))
            {
                return Json(new { error = "型式が決まりません。先に詳細型式を選んでください。" });
            }

            SeatConfigStore.Result result = SeatConfigStore.Create(_context, new SeatConfigInput
            {
                Airline = airline,
                Type = typeCode,
                Url = url,
                F = f,
                C = c,
                PY = py,
                P = p,
                J = j,
                Y = y,
            });

            return result.Error != null
                ? Json(new { error = result.Error })
                : Json(new { id = result.Id, name = result.Name, duplicated = result.Duplicated });
        }
    }
}