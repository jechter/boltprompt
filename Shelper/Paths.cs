using System.Reflection;
using NiceIO;

namespace Shelper;

internal static class Paths
{
    private static NPath ShelperDir { get; } = NPath.HomeDirectory.Combine("Library/Application Support/Shelper").MakeAbsolute();
    public static NPath LogDir { get; } = ShelperDir.Combine("Logs");
    public static NPath CommandsDir { get; } = ShelperDir.Combine("Commands");
    public static NPath History { get; } = ShelperDir.Combine("history");
    public static NPath FigAutoCompleteDir { get; } = Assembly.GetExecutingAssembly().Location.ToNPath().ParentContaining("autocomplete").Combine("autocomplete");
}