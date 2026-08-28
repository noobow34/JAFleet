using Microsoft.AspNetCore.Mvc;
using JAFleet.Models;
using JAFleet.Commons.EF;
using Noobow.Commons.Utils;
using Noobow.Commons.Constants;
using EnumStringValues;

namespace JAFleet.Controllers
{
    public class MessageController : Controller
    {

        private readonly JAFleetContext _context;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _services;

        public MessageController(JAFleetContext context, IConfiguration configuration, IServiceScopeFactory serviceScopeFactory)
        {
            _context = context;
            _configuration = configuration;
            _services = serviceScopeFactory;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> SendAsync(MessageModel model)
        {
            await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(), "【JA-Fleet from web】\n" +
                $"名前：{model.Name}\n" +
                $"返信先：{model.Reply}\n" +
                $"{model.Message}");
            _ = Task.Run(() =>
            {
                using var serviceScope = _services.CreateScope();
                using var context = serviceScope.ServiceProvider.GetService<JAFleetContext>()!;
                var m = new Message
                {
                    Sender = model.Name,
                    MessageDetail = model.Message,
                    ReplyTo = model.Reply,
                    MessageType = Commons.Constants.MessageType.WEB,
                    RecieveDate = DateTime.Now
                };
                context.Messages.Add(m);
                context.SaveChanges();
            });
            return Content("OK");
        }

    }
}
