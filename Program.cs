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
        private const string NOTION_DATABASE_DATE_PROPERTY = "NOTION_DATABASE_DATE_PROPERTY";

        static async Task Main()
        {
            // --- GitHub Actions の Secrets から環境変数を取得 ---
            string webhookUrl = Environment.GetEnvironmentVariable(DISCORD_WEBHOOK) ?? string.Empty;
            string notionToken = Environment.GetEnvironmentVariable(NOTION_TOKEN) ?? string.Empty;
            string databaseID = Environment.GetEnvironmentVariable(NOTION_DATABASE_ID) ?? string.Empty;
            string datePropertyName = Environment.GetEnvironmentVariable(NOTION_DATABASE_DATE_PROPERTY) ?? string.Empty;

            if (string.IsNullOrEmpty(webhookUrl))
            {
                Console.WriteLine("環境変数 DISCORD_WEBHOOK が設定されていません。");
                return;
            }
            if (string.IsNullOrEmpty(notionToken))
            {
                Console.WriteLine("環境変数 NOTION_TOKEN が設定されていません。");
                return;
            }
            if (string.IsNullOrEmpty(databaseID))
            {
                Console.WriteLine("環境変数 NOTION_DATABASE_ID が設定されていません。");
                return;
            }
            if (string.IsNullOrEmpty(datePropertyName))
            {
                Console.WriteLine("環境変数 NOTION_DATABASE_DATE_PROPARTY が設定されていません。");
                return;
            }

            // --- Notion クライアント作成 ---
            NotionClient notion = NotionClientFactory.Create(new ClientOptions
            {
                AuthToken = notionToken,
            });

            // --- データベースをクエリ ---
            var query = await notion.Databases.QueryAsync(databaseID, new DatabasesQueryParameters());
            List<IWikiDatabase> database = query.Results;

            if (database.Count == 0)
            {
                Console.WriteLine("データベースの要素がありません。");
                return;
            }

            // --- 日本時間を取得 ---
            DateTime nowTime = DateTime.UtcNow.AddHours(9);
            DateTime today = nowTime.Date;

            StringBuilder sb = new StringBuilder($"GitHub Actionsからのテスト通知です！ {nowTime:yyyy/MM/dd HH:mm:ss}");
            
            // 開始タスクと納期タスクの数を追跡
            int startTaskCount = 0;
            int endTaskCount = 0;

            // 開始タスク用と納期タスク用のStringBuilderを分離
            StringBuilder startTasksSb = new();
            StringBuilder endTasksSb = new();

            // 各ページを走査（開始タスク）
            foreach (var item in database)
            {
                if (item is not Page page) continue;

                // 日付プロパティ取得
                if (page.Properties.TryGetValue(datePropertyName, out var datePropertyValue) &&
                    datePropertyValue is DatePropertyValue dateProperty)
                {
                    // 開始日付を取得
                    DateTimeOffset? startOffset = dateProperty.Date?.Start;
                    DateTime? start = startOffset?.UtcDateTime;

                    // JST補正（UTC+9）
                    if (start.HasValue) start = start.Value.AddHours(9);

                    // JST補正（NotionはUTC基準）
                    if (start.HasValue) start = start.Value.ToUniversalTime().AddHours(9);

                    // ページタイトルを取得
                    string pageName = "(名称未設定)";
                    if (page.Properties.TryGetValue("名前", out var titlePropValue) &&
                        titlePropValue is TitlePropertyValue titleProperty)
                    {
                        pageName = string.Join("", titleProperty.Title.Select(t => t.PlainText));
                    }

                    // 開始タスクが今日の場合
                    if (start.HasValue && start.Value.Date == today)
                    {
                        startTasksSb.AppendLine($"\n🟢 開始タスク: {pageName}");
                        startTaskCount++;

                        // ページ本文を追加
                        await AppendPageContentAsync(startTasksSb, page, notion);
                    }
                }
            }

            // --- 各ページを走査（納期タスク） ---
            foreach (var item in database)
            {
                if (item is not Page page) continue;

                // 日付プロパティ取得
                if (page.Properties.TryGetValue(datePropertyName, out var datePropertyValue) &&
                    datePropertyValue is DatePropertyValue dateProperty)
                {
                    // 納期日付を取得
                    DateTimeOffset? endOffset = dateProperty.Date?.End;
                    DateTime? end = endOffset?.UtcDateTime;

                    // JST補正（UTC+9）
                    if (end.HasValue) end = end.Value.AddHours(9);

                    // JST補正（NotionはUTC基準）
                    if (end.HasValue) end = end.Value.ToUniversalTime().AddHours(9);

                    // ページタイトルを取得
                    string pageName = "(名称未設定)";
                    if (page.Properties.TryGetValue("名前", out var titlePropValue) &&
                        titlePropValue is TitlePropertyValue titleProperty)
                    {
                        pageName = string.Join("", titleProperty.Title.Select(t => t.PlainText));
                    }

                    // 納期タスクが今日の場合
                    if (end.HasValue && end.Value.Date == today)
                    {
                        endTasksSb.AppendLine($"\n🔴 納期タスク: {pageName}");
                        endTaskCount++;

                        // ページ本文を追加
                        await AppendPageContentAsync(endTasksSb, page, notion);
                    }
                }
            }

            // 開始タスクと納期タスクが一つもない場合は通知を送信しない
            if (startTaskCount == 0 && endTaskCount == 0)
            {
                Console.WriteLine("今日の開始タスクと納期タスクがありません。通知を送信しません。");
                return;
            }

            // 開始タスクと納期タスクをメインのStringBuilderに追加
            sb.Append(startTasksSb);
            sb.Append(endTasksSb);

            // --- Discordへ送信 ---
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

            Console.WriteLine($"Discord送信結果: {response.StatusCode}");
        }

        /// <summary>
        /// ページ本文をStringBuilderに追加する
        /// </summary>
        private static async Task AppendPageContentAsync(StringBuilder sb, Page page, NotionClient notion)
        {
            string pageContext = await GetAllContentAsync(page, notion);
            sb.AppendLine(new string('-', 10));
            sb.AppendLine(pageContext);
            sb.AppendLine(new string('-', 10));
            sb.AppendLine();
        }

        /// <summary>
        /// Notionページのブロックをすべて文字列化する
        /// </summary>
        private static async Task<string> GetAllContentAsync(Page page, NotionClient notion)
        {
            var sb = new StringBuilder();
            string startCursor = string.Empty;

            do
            {
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
                        return string.Concat(richTexts
                            .OfType<RichTextText>()
                            .Select(t => t.Text?.Content ?? ""));
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
                            string checkbox = todo.ToDo.IsChecked ? "[x]" : "[ ]";
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

                    // 子要素（HasChildren=true）の場合、再帰的に取得
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
