using JAFleet.Classes;
using JAFleet.Models;
using Newtonsoft.Json;
using SlackNet;

namespace JAFleet.Test
{
    [TestClass]
    public sealed class WorkingStatusSlackMessageTest
    {
        private static WorkingStatusNotifyJson SampleNotify() => new()
        {
            FinishedAt  = "2026/08/16 05:12:34",
            Elapsed     = "01:23:45",
            WaitSeconds = 1234.5,
            LogDate     = "20260816",
            Sections    =
            [
                new WorkingStatusNotifySectionJson
                {
                    Title = "整備入り",
                    Regs  =
                    [
                        new WorkingStatusNotifyRegJson { Reg = "JA801A" },
                        new WorkingStatusNotifyRegJson { Reg = "JA802A", Mark = "◎" },
                    ]
                },
                new WorkingStatusNotifySectionJson
                {
                    Title = "稼働が非稼働",
                    Regs  = [.. Enumerable.Range(1, 15).Select(i => new WorkingStatusNotifyRegJson { Reg = $"JA{i:000}J" })]
                },
            ]
        };

        /// <summary>SlackNetのシリアライズ結果がSlackのtableブロックの形になっていること</summary>
        [TestMethod]
        public void BuildBlocksAsTable()
        {
            var blocks = WorkingStatusSlackMessage.BuildBlocks(SampleNotify());
            var json = JsonConvert.SerializeObject(blocks, Default.JsonSettings(Default.SlackTypeResolver()).SerializerSettings);
            Console.WriteLine(json);

            var parsed = System.Text.Json.JsonDocument.Parse(json).RootElement;
            var table = parsed.EnumerateArray().Single(b => b.GetProperty("type").GetString() == "table");

            var rows = table.GetProperty("rows");
            Assert.AreEqual(3, rows.GetArrayLength());                                        // ヘッダー行＋2区分
            Assert.AreEqual(3, rows[0].GetArrayLength());                                     // 区分・件数・機体
            Assert.AreEqual("raw_text", rows[0][0].GetProperty("type").GetString());
            Assert.AreEqual("整備入り", rows[1][0].GetProperty("text").GetString());
            Assert.AreEqual("2", rows[1][1].GetProperty("text").GetString());
            Assert.AreEqual("rich_text", rows[1][2].GetProperty("type").GetString());
            Assert.AreEqual(3, table.GetProperty("column_settings").GetArrayLength());
            Assert.AreEqual("right", table.GetProperty("column_settings")[1].GetProperty("align").GetString());

            // 11件以上は先頭10件＋「ほかN件」
            var manyRegs = rows[2][2].GetProperty("elements")[0].GetProperty("elements");
            Assert.AreEqual("  ほか5件", manyRegs[manyRegs.GetArrayLength() - 1].GetProperty("text").GetString());

            // 1テーブルあたり全セル10,000文字までの制限内であること
            Assert.IsTrue(json.Length < 10000);
        }

        /// <summary>保存したJSONから通知を復元できること・旧形式のテキストはそのまま扱うこと</summary>
        [TestMethod]
        public void TryParseStoredLogDetail()
        {
            var stored = System.Text.Json.JsonSerializer.Serialize(SampleNotify());
            var notify = WorkingStatusSlackMessage.TryParse(stored);

            Assert.IsNotNull(notify);
            Assert.AreEqual("20260816", notify.LogDate);
            Assert.AreEqual(2, notify.Sections.Count);
            StringAssert.Contains(WorkingStatusSlackMessage.BuildText(notify), "整備入り:2件");
            StringAssert.Contains(WorkingStatusSlackMessage.BuildText(notify), "/WorkingCheckLog/Index/20260816|リンク");

            Assert.IsNull(WorkingStatusSlackMessage.TryParse("RefreshWorkingStatus正常終了:2026/08/16 05:12:34"));
            Assert.IsNull(WorkingStatusSlackMessage.TryParse("{壊れたJSON"));
        }

        /// <summary>変化なしの場合はテーブルを出さないこと</summary>
        [TestMethod]
        public void BuildBlocksWithoutSections()
        {
            var blocks = WorkingStatusSlackMessage.BuildBlocks(new WorkingStatusNotifyJson { LogDate = "20260816" });
            var json = JsonConvert.SerializeObject(blocks, Default.JsonSettings(Default.SlackTypeResolver()).SerializerSettings);

            Assert.IsFalse(json.Contains("\"table\""));
            StringAssert.Contains(json, "状態が変化した機体はありませんでした。");
        }
    }
}
