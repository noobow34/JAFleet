using JAFleet.Models;
using SlackNet.Blocks;
using System.Text.Json;

namespace JAFleet.Batch
{
    /// <summary>
    /// 稼働チェック結果のSlack通知メッセージ（BlockKit）を組み立てる。
    /// 通知テキストは <see cref="WorkingStatusNotifyJson"/> としてログに保存し、
    /// RefreshWorkingStatusAndPhoto／NotifyWorkingStatusJobの双方から同じ見た目で投稿する。
    /// </summary>
    public static class WorkingStatusSlackMessage
    {
        private const string SITE_URL           = "https://ja-fleet.noobow.me";
        /// <summary>1セルに表示するレジの最大数（Slackのテーブルは全セル合計10,000文字まで）</summary>
        private const int    MAX_REG_IN_CELL    = 10;

        /// <summary>JSONとして解釈できるならデシリアライズする（旧形式のテキストはnull）</summary>
        public static WorkingStatusNotifyJson? TryParse(string? logDetail)
        {
            if (string.IsNullOrWhiteSpace(logDetail) || !logDetail.TrimStart().StartsWith('{'))
                return null;

            try
            {
                return JsonSerializer.Deserialize<WorkingStatusNotifyJson>(logDetail);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>通知のフォールバックテキスト（通知バナー・旧形式互換用）</summary>
        public static string BuildText(WorkingStatusNotifyJson notify)
        {
            var sections = string.Concat(notify.Sections.Select(s => $"{s.Title}:{s.Regs.Count}件\n"));
            return $"RefreshWorkingStatus正常終了:{notify.FinishedAt}\n" +
                   $"処理時間: {notify.Elapsed},待機秒数: {notify.WaitSeconds}\n" +
                   sections +
                   $"<{LogUrl(notify)}|リンク>";
        }

        /// <summary>結果一覧をTableにしたBlockKitメッセージ</summary>
        public static IList<Block> BuildBlocks(WorkingStatusNotifyJson notify)
        {
            var blocks = new List<Block>
            {
                new HeaderBlock { Text = new PlainText(":airplane: 稼働チェック結果") { Emoji = true } },
                new ContextBlock
                {
                    Elements = new List<IContextElement>
                    {
                        new Markdown($"*完了* {notify.FinishedAt}　*処理時間* {notify.Elapsed}　*待機* {notify.WaitSeconds}秒")
                    }
                }
            };

            if (notify.Sections.Count == 0)
            {
                blocks.Add(new SectionBlock { Text = new Markdown("状態が変化した機体はありませんでした。") });
            }
            else
            {
                blocks.Add(BuildTable(notify.Sections));
            }

            blocks.Add(new ContextBlock
            {
                Elements = new List<IContextElement>
                {
                    new Markdown($"<{LogUrl(notify)}|稼働チェックログを開く>")
                }
            });

            return blocks;
        }

        private static TableBlock BuildTable(List<WorkingStatusNotifySectionJson> sections)
        {
            var rows = new List<IList<TableCell>>
            {
                new List<TableCell>
                {
                    new RawTextCell { Text = "区分" },
                    new RawTextCell { Text = "件数" },
                    new RawTextCell { Text = "機体" },
                }
            };

            foreach (var s in sections)
            {
                rows.Add(new List<TableCell>
                {
                    new RawTextCell { Text = s.Title },
                    new RawTextCell { Text = s.Regs.Count.ToString() },
                    RegistrationCell(s.Regs),
                });
            }

            return new TableBlock
            {
                Rows = rows,
                ColumnSettings = new List<TableColumnSettings>
                {
                    new() { Align = ColumnAlignment.Left },
                    new() { Align = ColumnAlignment.Right },
                    new() { Align = ColumnAlignment.Left, IsWrapped = true },
                }
            };
        }

        /// <summary>レジを機体詳細へのリンクにして1セルに並べる</summary>
        private static TableCell RegistrationCell(List<WorkingStatusNotifyRegJson> regs)
        {
            if (regs.Count == 0)
                return new RawTextCell { Text = "-" };

            var elements = new List<RichTextSectionElement>();
            foreach (var r in regs.Take(MAX_REG_IN_CELL))
            {
                if (elements.Count > 0)
                    elements.Add(new RichTextText { Text = "  " });
                if (!string.IsNullOrEmpty(r.Mark))
                    elements.Add(new RichTextText { Text = r.Mark });
                elements.Add(new RichTextLink { Url = $"{SITE_URL}/AD/{r.Reg}", Text = r.Reg });
            }

            if (regs.Count > MAX_REG_IN_CELL)
                elements.Add(new RichTextText { Text = $"  ほか{regs.Count - MAX_REG_IN_CELL}件" });

            return new RichTextCell
            {
                Elements = new List<RichTextElement> { new RichTextSection { Elements = elements } }
            };
        }

        private static string LogUrl(WorkingStatusNotifyJson notify) =>
            $"{SITE_URL}/WorkingCheckLog/Index/{notify.LogDate}";
    }
}
