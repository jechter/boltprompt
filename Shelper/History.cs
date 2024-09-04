using NiceIO;

namespace Shelper;

public static class History
{
    private static readonly NPath Path = "history";
    private static string[]? _commands; 
    
    public static void AddCommandToHistory(string command)
    {
        if (string.IsNullOrEmpty(command)) return;
        _commands = Commands.Where(c => c != command).Append(command).ToArray();
        Path.MakeAbsolute().WriteAllLines(_commands);
    }

    public static string[] Commands =>
        _commands ??= !Path.FileExists() ? [] : Path.MakeAbsolute().ReadAllLines().ToArray();
}