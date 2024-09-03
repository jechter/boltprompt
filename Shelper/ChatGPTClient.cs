using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shelper;

public class ChatGPTClient
{
    private static readonly HttpClient client = new ();
    private static readonly string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    private const string apiUrl = "https://api.openai.com/v1/chat/completions";

    record GPTMessage
    {
        [JsonInclude]
        public string role;
        [JsonInclude]
        public string content;
    }

    public static async Task<bool> GetBooleanReply(params string[] prompt)
    {
        var stringReply = await GetReply(prompt.Append("Reply with just yes or no.").ToArray());
        return stringReply.ToLower().Contains("yes");
    }

    public static async Task<string> GetReply(params string[] prompt)
    {
        var messages = new List<GPTMessage> { new GPTMessage { role = "system", content = "You are ChatGPT, a helpful assistant." } };
        messages.AddRange(prompt.Select(s => new GPTMessage { role = "user", content = s }));
        
        var requestBody = new
        {
            model = "gpt-3.5-turbo",//"gpt-3.5-turbo",//"gpt-4", // or "gpt-3.5-turbo"
            messages,
            max_tokens = 4096,
            n = 1,
            stop = (string)null,
            temperature = 0.7
        };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        Logger.Log(Logger.GPT, "Request:");
        Logger.Log(Logger.GPT, JsonSerializer.Serialize(requestBody));

        var response = await client.PostAsJsonAsync(apiUrl, requestBody);

        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            Logger.Log(Logger.GPT, "Response:");
            Logger.Log(Logger.GPT, responseContent);
            var responseData = JsonSerializer.Deserialize<JsonElement>(responseContent);

            string chatResponse = responseData
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return chatResponse;
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Logger.Log(Logger.GPT, "Error:");
            Logger.Log(Logger.GPT, errorContent);
            return errorContent;
        }
    }
}