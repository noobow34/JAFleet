using EnumStringValues;
using JAFleet.Commons.Constants;
using JAFleet.Commons.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Noobow.Commons.Constants;
using Noobow.Commons.Utils;
using JAFleet.Batch;

namespace JAFleet.Controllers
{
    public class BatchController : Controller
    {

        private readonly IServiceScopeFactory _services;

        public BatchController(IServiceScopeFactory serviceScopeFactory) => _services = serviceScopeFactory;

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> RefreshWorkingStatusAndPhotoAsync(int? interval)
        {
            if (JAFleet.Batch.RefreshWorkingStatusAndPhoto.Processing)
            {
                await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), "RefreshWorkingStatusAndPhoto 二重起動を検出");
                return Content("Now Processing!!");
            }

            _ = Task.Run(() =>
            {
                using var serviceScope = _services.CreateScope();
                IEnumerable<AircraftView> targetReg;
                using JAFleetContext? context = serviceScope.ServiceProvider.GetService<JAFleetContext>();
                targetReg = context!.AircraftViews.Where(a => a.OperationCode != OperationCode.RETIRE_UNREGISTERED).AsNoTracking().ToArray().OrderBy(r => Guid.NewGuid());
                var check = new RefreshWorkingStatusAndPhoto(targetReg, interval ?? 15);
                _ = check.ExecuteCheckAsync(true);
            });

            return Content("RefreshWorkingStatusAndPhoto Launch!");
        }

        [Authorize]
        public async Task<IActionResult> RefreshPhotoAsync(int? interval,int mode)
        {
            if (JAFleet.Batch.RefreshPhoto.Processing)
            {
                await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), "RefreshPhoto 二重起動を検出");
                return Content("Now Processing!!");
            }

            _ = Task.Run(() =>
            {
                using var serviceScope = _services.CreateScope();
                IEnumerable<AircraftView> targetReg;
                using JAFleetContext? context = serviceScope.ServiceProvider.GetService<JAFleetContext>();
                targetReg =  mode switch
                {
                    2 => context!.AircraftViews.Where(a => a.OperationCode != OperationCode.RETIRE_UNREGISTERED).AsNoTracking().ToArray().OrderBy(r => Guid.NewGuid()),
                    3 => context!.AircraftViews.Where(a => a.OperationCode == OperationCode.RETIRE_UNREGISTERED).AsNoTracking().ToArray().OrderBy(r => Guid.NewGuid()),
                    _ => context!.AircraftViews.AsNoTracking().ToArray().OrderBy(r => Guid.NewGuid())
                };
                var check = new RefreshPhoto(targetReg, interval ?? 15);
                _ = check.ExecuteRefreshAsync();
            });

            return Content("RefreshPhoto Launch!");
        }
    }
}