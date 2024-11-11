using System.Text.Json;
using System.Text.Json.Serialization;
using CliWrap;
using CliWrap.Buffered;
using NiceIO;

namespace boltprompt;

public static class TerminalUtility
{
    private static string? Terminal => Environment.GetEnvironmentVariable("TERM_PROGRAM");
    private static string TerminalSession => Environment.GetEnvironmentVariable("TERM_SESSION_ID") ?? "Unknown";

    private const string AppleTerminal = "Apple_Terminal";
    private const string iTerm = "iTerm.app";
    

    public static bool IsSupportedTerminal => Terminal is AppleTerminal or iTerm;


    private record TerminalConfig
    {
        [JsonInclude]
        public string Font = "";
        [JsonInclude]
        public bool HasPowerlineSymbols;
    }

    private static TerminalConfig? _terminalConfig;

    private static void CleanupTerminalConfigs()
    {
        foreach (var f in Paths.TerminalConfig.Files())
        {
            if (DateTime.UtcNow - File.GetLastAccessTimeUtc(f.ToString()) > TimeSpan.FromDays(1))
                f.Delete();
        }
    }
    
    private static TerminalConfig? GetTerminalConfig(string key)
    {
        if (_terminalConfig != null)
            return _terminalConfig;
        var path = Paths.TerminalConfig.Combine(key);
        _terminalConfig = path.FileExists() ? JsonSerializer.Deserialize<TerminalConfig>(path.ReadAllText()) : null;
        return _terminalConfig;
    }

    private static void WriteTerminalConfig(TerminalConfig config, string key)
    {
        var path = Paths.TerminalConfig.Combine(key);
        path.WriteAllText(JsonSerializer.Serialize(config));
        _terminalConfig = config;
        CleanupTerminalConfigs();
    }
    
    public static void SetupTerminal()
    {
        if (!IsSupportedTerminal)
            throw new NotSupportedException($"boltprompt only knows how to configure fonts for Terminal.app or iTerm.app. You are using {Terminal}, which we don't know how to set up.");
        Cli.Wrap("bash")
            .WithArguments(Paths.boltpromptSupportFilesDir.Combine("setup-terminal.sh").ToString())
            .WithWorkingDirectory(Paths.boltpromptSupportFilesDir.ToString())
            .ExecuteBufferedAsync()
            .GetAwaiter()
            .GetResult();
        switch (Terminal)
        {
            case AppleTerminal:
                Console.WriteLine("Terminal.app has been set up for boltprompt.");
                break;
            case iTerm:
                Console.WriteLine("iTerm2 has been set up for boltprompt. Please restart iTerm2 for the changes to take effect.");
                break;
        }
    }

    private static string? _currentTerminalFont;

    private static async Task<string?> GetCurrentTerminalFont()
    {
        if (_currentTerminalFont != null)
            return _currentTerminalFont;
        if (!IsSupportedTerminal)
            return null;
        var command = Terminal switch
        {
            AppleTerminal => Cli.Wrap("osascript")
                .WithArguments("get-terminal-font.scpt")
                .WithWorkingDirectory(Paths.boltpromptSupportFilesDir.ToString()),
            iTerm => Cli.Wrap("/usr/libexec/PlistBuddy")
                .WithArguments(
                    $"-c \"Print :\\\"New Bookmarks\\\":0:\\\"Normal Font\\\"\"  {NPath.HomeDirectory}/Library/Preferences/com.googlecode.iterm2.plist"),
            _ => throw new InvalidOperationException()
        };
        var result = (await command.ExecuteBufferedAsync()).StandardOutput;
        if (Terminal == iTerm)
            result = result[..result.LastIndexOf(' ')];
        _currentTerminalFont = result;
        return _currentTerminalFont;
    }

    private static async Task UpdateTerminalConfig()
    {
        var font = await GetCurrentTerminalFont();
        if (font == null) return;
        var config = new TerminalConfig
        {
            Font = font,
            HasPowerlineSymbols = FontUtility.FontHasGlyph(font, '\uE0B0')
        };
        WriteTerminalConfig(config, TerminalSession);    
        WriteTerminalConfig(config, Terminal ?? "Unknown");    
    }
    
    public static bool CurrentTerminalHasPowerlineSymbol()
    {
        var config = GetTerminalConfig(TerminalSession); 
        if (config != null)
            return config.HasPowerlineSymbols;
        config = GetTerminalConfig(Terminal ?? "Unknown");
        _terminalConfig ??= new();
        UpdateTerminalConfig().GetAwaiter();
        return config is { HasPowerlineSymbols: true };
    }
}