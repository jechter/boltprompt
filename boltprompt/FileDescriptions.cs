using CliWrap;
using CliWrap.Buffered;

namespace boltprompt;

public static class FileDescriptions
{
    private static readonly Dictionary<string, Task<string>> FileDescriptionCache = new();

    public static event Action FileDescriptionLoaded = () => {};
    static async Task<string> RequestFileDescription(string path)
    {
        var commandResult = await Cli.Wrap("file")
            .WithArguments(new string[] { "-b", Suggestor.UnescapeFileName(path).Trim() })
            .ExecuteBufferedAsync();
        return commandResult.StandardOutput.Split(Environment.NewLine)[0];
    }
    
    public static string? GetFileDescription(string path)
    {
        if (FileDescriptionCache.TryGetValue(path, out var fileDescription))
            return fileDescription.IsCompletedSuccessfully ? fileDescription.Result : null;

        var task = RequestFileDescription(path);
        FileDescriptionCache[path] = task;
        task.ContinueWith(_ =>
        {
            FileDescriptionLoaded.Invoke();
        });
        return null;
    }
}