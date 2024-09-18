using System.Reflection;
using NiceIO;

namespace Shelper;

internal static class Paths
{
    private static NPath ShelperUserDir { get; } = NPath.HomeDirectory.Combine("Library/Application Support/Shelper").MakeAbsolute();
    public static NPath ShelperInstallDir { get; } = Assembly.GetExecutingAssembly().Location.ToNPath().ParentContaining("Shelper.sln");
    public static NPath LogDir { get; } = ShelperUserDir.Combine("Logs");
    public static NPath GeneratedCommandsDir { get; } = ShelperUserDir.Combine("Commands");
    public static NPath BuiltInCommandsDir { get; } = ShelperInstallDir.Combine("Commands");

    public static NPath History { get; } = ShelperUserDir.Combine("history");
    public static NPath FigAutoCompleteDir { get; } = ShelperInstallDir.Combine("autocomplete");
}