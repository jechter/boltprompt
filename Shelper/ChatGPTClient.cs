using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shelper;

public static class ChatGptClient
{
    private static readonly HttpClient Client = new ();
    private static readonly string? ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

    public static bool IsAvailable => ApiKey != null;
    public record GptMessage(string role, string content)
    {
        [JsonInclude]
        public string role = role;
        [JsonInclude]
        public string content = content;
    }

    public static async Task<bool> GetBooleanReply(params string[] prompt)
    {
        var stringReply = await GetReply(prompt.Append("Reply with just yes or no.").ToArray());
        return stringReply.Contains("yes", StringComparison.InvariantCultureIgnoreCase);
    }

    public static async Task<string> GetReply(params string[] prompt)
    {
        var messages = new List<GptMessage> {new( "system", "You are ChatGPT, a helpful assistant.")};
        messages.AddRange(prompt.Select(s => new GptMessage("user", s)));
        
        var requestBody = new
        {
            model = "gpt-3.5-turbo",//"gpt-3.5-turbo",//"gpt-4", // or "gpt-3.5-turbo"
            messages,
            max_tokens = 4096,
            n = 1,
            stop = (string?)null,
            temperature = 0.7
        };

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        Logger.Log(Logger.Gpt, "Request:");
        Logger.Log(Logger.Gpt, JsonSerializer.Serialize(requestBody));

        var response = await Client.PostAsJsonAsync(ApiUrl, requestBody);

        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            Logger.Log(Logger.Gpt, "Response:");
            Logger.Log(Logger.Gpt, responseContent);
            var responseData = JsonSerializer.Deserialize<JsonElement>(responseContent);

            var chatResponse = responseData
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (chatResponse == null)
                throw new InvalidDataException("Could not read GPT response JSON");
            return chatResponse;
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        Logger.Log(Logger.Gpt, "Error:");
        Logger.Log(Logger.Gpt, errorContent);
        throw new WebException("GPT returned an error, see log for details.");
    }
}