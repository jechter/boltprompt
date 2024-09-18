using System.Reflection;
using NiceIO;

namespace Shelper;

internal static class Paths
{
    private static NPath ShelperUserDir { get; } = NPath.HomeDirectory.Combine("Library/Application Support/Shelper").MakeAbsolute();
    public static NPath ShelperSupportFilesDir { get; } = Assembly.GetExecutingAssembly().Location.ToNPath().ParentContaining("ShelperSupportFiles").Combine("ShelperSupportFiles");
    public static NPath LogDir { get; } = ShelperUserDir.Combine("Logs");
    public static NPath GeneratedCommandsDir { get; } = ShelperUserDir.Combine("Commands");
    public static NPath BuiltInCommandsDir { get; } = ShelperSupportFilesDir.Combine("Commands");
    public static NPath History { get; } = ShelperUserDir.Combine("history");
    public static NPath FigAutoCompleteDir { get; } = ShelperSupportFilesDir.Combine("autocomplete");
}