using System.Reflection;
using System.Runtime.InteropServices;
using NiceIO;

namespace boltprompt;

internal static class Paths
{
    static Paths()
    {
        OSConfigDir = NPath.HomeDirectory.Combine(RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? "Library/Application Support"
            : ".config");
    }
    
    private static NPath OSConfigDir { get; }
    private static NPath boltpromptUserDir => OSConfigDir.Combine("boltprompt");
    public static NPath boltpromptSupportFilesDir { get; } = Assembly.GetExecutingAssembly().Location.ToNPath().ParentContaining("boltpromptSupportFiles").Combine("boltpromptSupportFiles");
    public static NPath LogDir => boltpromptUserDir.Combine("Logs");
    public static NPath GeneratedCommandsDir => boltpromptUserDir.Combine("Commands");
    public static NPath BuiltInCommandsDir { get; } = boltpromptSupportFilesDir.Combine("Commands");
    public static NPath History => boltpromptUserDir.Combine("history");
    public static NPath Configuration => boltpromptUserDir.Combine("configuration");    
    public static NPath TerminalConfig => boltpromptUserDir.Combine("TerminalConfig");
}