using NiceIO;

namespace Shelper;

public static class History
{
    private static string[]? _commands; 
    
    public static void AddCommandToHistory(string command)
    {
        if (string.IsNullOrEmpty(command)) return;
        _commands = Commands.Where(c => c != command).Append(command).ToArray();
        Paths.History.WriteAllLines(_commands);
    }

    public static string[] Commands =>
        _commands ??= !Paths.History.FileExists() ? [] : Paths.History.ReadAllLines().ToArray();

    internal static void LoadTestHistory(string[] commands)
    {
        _commands = commands;
    }
}