using EnumStringValues;
using JAFleet.Classes;
using JAFleet.Models;
using Noobow.Commons.Constants;
using Noobow.Commons.Utils;

namespace JAFleet.Test
{
    /// <summary>
    /// 稼働チェック結果の通知を実際にSlackへ投稿して見た目を確認する手動テスト。
    /// 環境変数 SLACK_BOT_TOKEN が設定されているときだけ実行される。
    /// </summary>
    [TestClass]
    public sealed class WorkingStatusSlackNotifyTest
    {
        [TestMethod]
        public async Task PostSampleNotifyAsync()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SLACK_BOT_TOKEN")))
            {
                Assert.Inconclusive("SLACK_BOT_TOKENが未設定のためスキップ");
                return;
            }

            var notify = new WorkingStatusNotifyJson
            {
                FinishedAt  = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"),
                Elapsed     = "02:37:14.8210553",
                WaitSeconds = 8123.4,
                LogDate     = DateTime.Now.ToString("yyyyMMdd"),
                Sections =
                [
                    new WorkingStatusNotifySectionJson { Title = "テストレジが稼働", Regs = [new() { Reg = "JA26MJ" }] },
                    new WorkingStatusNotifySectionJson
                    {
                        Title = "運用中非稼働が稼働",
                        Regs  = [new() { Reg = "JA801A" }, new() { Reg = "JA605J" }, new() { Reg = "JA334J", Mark = "☆" }]
                    },
                    new WorkingStatusNotifySectionJson
                    {
                        Title = "稼働が非稼働",
                        Regs  = [.. new[] { "JA789A", "JA652J", "JA341J", "JA820P", "JA615A", "JA104X",
                                            "JA502N", "JA773J", "JA231A", "JA860B", "JA905F", "JA318J" }
                                    .Select(r => new WorkingStatusNotifyRegJson { Reg = r })]
                    },
                    new WorkingStatusNotifySectionJson { Title = "整備入り", Regs = [new() { Reg = "JA602A" }, new() { Reg = "JA871N", Mark = "◎" }] },
                    new WorkingStatusNotifySectionJson { Title = "整備中",   Regs = [new() { Reg = "JA710A" }, new() { Reg = "JA218J" }] },
                ]
            };

            await SlackUtil.PostAsync(
                SlackChannelEnum.jafleet.GetStringValue(),
                WorkingStatusSlackMessage.BuildText(notify),
                WorkingStatusSlackMessage.BuildBlocks(notify));
        }
    }
}
