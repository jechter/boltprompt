using NiceIO;

namespace Shelper;

public class History
{
    private static NPath Path = "history";
    
    public static void AddCommandToHistory(string command)
    {
        if (!string.IsNullOrEmpty(command))
            File.AppendAllText(Path.ToString(), $"{command}\n");
    }

    public static string[] GetCommands()
    {
        return !Path.FileExists() ? [] : Path.MakeAbsolute().ReadAllLines().Distinct().ToArray();
    }
}