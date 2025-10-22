using Discord.WebSocket;
using Notion.Client;
using System.Text;
using System.Text.Json;

namespace SinfoniaStudio.Master
{
    internal class Program
    {
        private const string DISCORD_WEBHOOK = "DISCORD_WEBHOOK";
        private const string NOTION_TOKEN = "NOTION_TOKEN";
        private const string NOTION_DATABASE_ID = "NOTION_DATABASE_ID";

        static async Task Main()
        {
            // GitHub Secrets などで渡されたWebhook URLを取得
            string webhookUrl = Environment.GetEnvironmentVariable(DISCORD_WEBHOOK) ?? string.Empty;

            if (string.IsNullOrEmpty(webhookUrl))
            {
                Console.WriteLine("環境変数 DISCORD_WEBHOOK が設定されていません。");
                return;
            }

            NotionClient notion = NotionClientFactory.Create(new ClientOptions
            {
                AuthToken = Environment.GetEnvironmentVariable(NOTION_TOKEN) ?? string.Empty,
            });

            var query = await notion.Databases.QueryAsync(
                Environment.GetEnvironmentVariable(NOTION_DATABASE_ID) ?? string.Empty,
                new DatabasesQueryParameters());

            List<IWikiDatabase> database = query.Results;

            if (database.Count <= 0)
            {
                Console.WriteLine("データベースの要素がありません。");
                return;
            }

            StringBuilder sb = new StringBuilder($"GitHub Actionsからのテスト通知です！ {DateTime.Now}");

            for (int i = 0; i < database.Count; i++)
            {
                var result = query.Results[i];

                // Page 型にキャスト
                if (result is Page page)
                {
                    string pageContext = await GetAllContentAsync(page, notion);

                    //パスを生成する。
                    string pageId = page.Id;

                    string oldContext = string.Empty;

                    sb.AppendLine();
                    sb.AppendLine(new string('-', 10));
                    sb.AppendLine(pageContext);
                    sb.AppendLine(new string('-', 10));
                }
            }

            using var client = new HttpClient();

            var payload = new
            {
                content = sb.ToString()
            };

            var json = JsonSerializer.Serialize(payload);
            var response = await client.PostAsync(
                webhookUrl,
                new StringContent(json, Encoding.UTF8, "application/json")
            );
        }

        /// <summary>
        ///     Notionのブロックを文字列にする。
        /// </summary>
        /// <param name="page"></param>
        /// <param name="notion"></param>
        /// <returns></returns>
        private static async Task<string> GetAllContentAsync(Page page, NotionClient notion)
        {
            var sb = new StringBuilder();
            string startCursor = null;

            do
            {
                // ページのブロックを取得（Pagination対応）
                var response = await notion.Blocks.RetrieveChildrenAsync(new BlockRetrieveChildrenRequest
                {
                    BlockId = page.Id,
                    StartCursor = startCursor,
                    PageSize = 100
                });

                foreach (var block in response.Results)
                {
                    string GetText(IEnumerable<RichTextBase> richTexts)
                    {
                        var rtSb = new StringBuilder();
                        foreach (var rt in richTexts)
                        {
                            if (rt is RichTextText txt && txt.Text != null)
                                rtSb.Append(txt.Text.Content);
                        }
                        return rtSb.ToString();
                    }

                    switch (block.Type)
                    {
                        case BlockType.Paragraph:
                            var para = (ParagraphBlock)block;
                            sb.AppendLine(GetText(para.Paragraph.RichText));
                            break;

                        case BlockType.Heading_1:
                            var h1 = (HeadingOneBlock)block;
                            sb.AppendLine($"# {GetText(h1.Heading_1.RichText)}");
                            break;

                        case BlockType.Heading_2:
                            var h2 = (HeadingTwoBlock)block;
                            sb.AppendLine($"## {GetText(h2.Heading_2.RichText)}");
                            break;

                        case BlockType.Heading_3:
                            var h3 = (HeadingThreeBlock)block;
                            sb.AppendLine($"### {GetText(h3.Heading_3.RichText)}");
                            break;

                        case BlockType.ToDo:
                            var todo = (ToDoBlock)block;
                            bool checkedFlag = todo.ToDo.IsChecked;
                            string checkbox = checkedFlag ? "[x]" : "[ ]";
                            sb.AppendLine($"{checkbox} {GetText(todo.ToDo.RichText)}");
                            break;

                        case BlockType.BulletedListItem:
                            var bullet = (BulletedListItemBlock)block;
                            sb.AppendLine($"・{GetText(bullet.BulletedListItem.RichText)}");
                            break;

                        case BlockType.NumberedListItem:
                            var num = (NumberedListItemBlock)block;
                            sb.AppendLine($"- {GetText(num.NumberedListItem.RichText)}");
                            break;

                        case BlockType.Quote:
                            var quote = (QuoteBlock)block;
                            sb.AppendLine($"> {GetText(quote.Quote.RichText)}");
                            break;

                        default:
                            sb.AppendLine($"[未対応ブロック: {block.Type}]");
                            break;
                    }

                    // 子要素（HasChildren = true の場合）は再帰取得。
                    if (block.HasChildren)
                    {
                        sb.AppendLine(await GetAllContentAsync(await notion.Pages.RetrieveAsync(block.Id), notion));
                    }
                }

                startCursor = response.NextCursor;

            } while (startCursor != null);

            return sb.ToString();
        }

    }
}
