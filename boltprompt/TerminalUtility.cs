using System.Text.Json;
using System.Text.Json.Serialization;
using CliWrap;
using CliWrap.Buffered;
using NiceIO;

namespace boltprompt;

public static class TerminalUtility
{
    private static string? Terminal => Environment.GetEnvironmentVariable("TERM_PROGRAM");
    public static string TerminalSession { get; } = Environment.GetEnvironmentVariable("TERM_SESSION_ID") ?? "Unknown";

    private const string AppleTerminal = "Apple_Terminal";
    private const string iTerm = "iTerm.app";
    private const string Ghostty = "ghostty";
    

    public static bool TerminalHasBoltpromptConfigurationAction => Terminal is AppleTerminal or iTerm;
    private static bool CanGetTerminalScrollbackBuffer => Terminal is AppleTerminal or iTerm;
    private static bool CanGetTerminalFonts => Terminal is AppleTerminal or iTerm;


    private record TerminalConfig
    {
        [JsonInclude]
        public string Font = "";
        [JsonInclude]
        public bool HasNerdFont;
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
        if (!TerminalHasBoltpromptConfigurationAction)
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
        Paths.TerminalConfig.Delete();
    }

    private static string? _currentTerminalFont;

    private static async Task<string?> GetCurrentTerminalFont()
    {
        if (_currentTerminalFont != null)
            return _currentTerminalFont;
        if (!CanGetTerminalFonts)
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
            HasNerdFont = FontUtility.FontHasGlyph(font, '\uE0B0')
        };
        WriteTerminalConfig(config, TerminalSession);
        WriteTerminalConfig(config, Terminal ?? "Unknown");
    }
    
    public static bool CurrentTerminalHasNerdFont()
    {
        if (Terminal == Ghostty)
            return true;
        var config = GetTerminalConfig(TerminalSession); 
        if (config != null)
            return config.HasNerdFont;
        config = GetTerminalConfig(Terminal ?? "Unknown");
        _terminalConfig ??= new();
        UpdateTerminalConfig().GetAwaiter();
        return config is { HasNerdFont: true };
    }
    
    public static bool CurrentTerminalHasCharacter(string str, int index)
    {
        var codePoint = char.ConvertToUtf32(str, index);
        var isNerdFontExtra = codePoint is >= 0xE000 and <= 0xF8FF or >= 0xF0000 and <= 0xFFFFD or >= 0x100000 and <= 0x10FFFD;
        return !isNerdFontExtra || CurrentTerminalHasNerdFont();
    }
    
    public static async Task<string> GetCurrentTerminalBuffer()
    {           
        if (Environment.GetEnvironmentVariable("TMUX") != null)
        {
            var result = await Cli.Wrap("tmux")
                .WithArguments(["capture-pane", "-S", "-1000", "-p"])
                .ExecuteBufferedAsync();
            return result.StandardOutput;
        }

        if (!CanGetTerminalScrollbackBuffer)
            return "";

        var tmpLog = Path.GetTempFileName();
        await Cli.Wrap("osascript")
            .WithArguments([Terminal switch
            {
                AppleTerminal => "capture-terminal.scpt",
                iTerm => "capture-iterm.scpt",
                _ => throw new InvalidOperationException()
            }, tmpLog])
            .WithWorkingDirectory(Paths.boltpromptSupportFilesDir.ToString())
            .ExecuteBufferedAsync();
        return await File.ReadAllTextAsync(tmpLog);
    }
}