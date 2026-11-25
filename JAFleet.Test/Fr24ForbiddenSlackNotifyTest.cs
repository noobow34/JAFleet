using EnumStringValues;
using JAFleet.Batch;
using Noobow.Commons.Constants;
using Noobow.Commons.Utils;

namespace JAFleet.Test
{
    /// <summary>
    /// FR24から403が返って巡回を中止したときの通知を、実際にSlackへ投稿して見た目を確認する手動テスト。
    /// JAFLEET_RUN_MANUAL_TESTS=1 で、環境変数 SLACK_BOT_TOKEN が設定されているときだけ実行される（<see cref="ManualTest"/>）。
    /// 本物の中止と見分けられるよう、先頭に【投稿テスト】を付ける。
    /// </summary>
    [TestClass]
    [TestCategory(ManualTest.CATEGORY)]
    public sealed class Fr24ForbiddenSlackNotifyTest
    {
        [TestMethod]
        public async Task PostForbiddenNotifyAsync()
        {
            ManualTest.RequireOptIn();
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SLACK_BOT_TOKEN")))
            {
                Assert.Inconclusive("SLACK_BOT_TOKENが未設定のためスキップ");
                return;
            }

            DateTime now = DateTime.Now;
            await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(),
                "【投稿テスト】\n" + Fr24AccessCheck.BuildMessage("RefreshWorkingStatus", "JA841A", now));
            await SlackUtil.PostAsync(SlackChannelEnum.jafleet.GetStringValue(),
                "【投稿テスト】\n" + Fr24AccessCheck.BuildMessage("RefreshPhoto", "JA801A", now));
        }
    }
}
