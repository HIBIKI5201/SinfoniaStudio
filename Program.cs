using System.Text;
using System.Text.Json;

namespace SinfoniaStudio.Master
{
    internal class Program
    {
        static async Task Main()
        {
            // GitHub Secrets などで渡されたWebhook URLを取得
            string webhookUrl = Environment.GetEnvironmentVariable("DiscordWebhook") ?? string.Empty;

            if (string.IsNullOrEmpty(webhookUrl))
            {
                Console.WriteLine("環境変数 DISCORD_WEBHOOK が設定されていません。");
                return;
            }

            using var client = new HttpClient();

            var payload = new
            {
                content = $"GitHub Actionsからのテスト通知です！ {DateTime.Now}"
            };

            var json = JsonSerializer.Serialize(payload);
            var response = await client.PostAsync(
                webhookUrl,
                new StringContent(json, Encoding.UTF8, "application/json")
            );

            Console.WriteLine($"送信結果: {response.StatusCode}");
        }
    }
}