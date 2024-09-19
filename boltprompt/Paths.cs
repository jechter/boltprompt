using System.Reflection;
using NiceIO;

namespace boltprompt;

internal static class Paths
{
    private static NPath boltpromptUserDir { get; } = NPath.HomeDirectory.Combine("Library/Application Support/boltprompt").MakeAbsolute();
    public static NPath boltpromptSupportFilesDir { get; } = Assembly.GetExecutingAssembly().Location.ToNPath().ParentContaining("boltpromptSupportFiles").Combine("boltpromptSupportFiles");
    public static NPath LogDir { get; } = boltpromptUserDir.Combine("Logs");
    public static NPath GeneratedCommandsDir { get; } = boltpromptUserDir.Combine("Commands");
    public static NPath BuiltInCommandsDir { get; } = boltpromptSupportFilesDir.Combine("Commands");
    public static NPath History { get; } = boltpromptUserDir.Combine("history");
    public static NPath FigAutoCompleteDir { get; } = boltpromptSupportFilesDir.Combine("autocomplete");
}