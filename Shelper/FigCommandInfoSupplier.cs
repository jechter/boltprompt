using System.Text.Json;
using System.Text.Json.Serialization;
using CliWrap;
using CliWrap.Buffered;
using NiceIO;

namespace Shelper;

internal record FigCommandInfo
{
    [JsonInclude]
    [JsonConverter(typeof(ArrayOrSingleValueConverter<string>))]
    public string[] name = [];
    [JsonInclude]
    public string? description;
    [JsonInclude]
    [JsonConverter(typeof(ArrayOrSingleValueConverter<FigArg>))]
    public FigArg[]? args;
    [JsonInclude]
    public FigOption[]? options;
    [JsonInclude]
    [JsonConverter(typeof(ArrayOrSingleValueConverter<FigCommandInfo>))]
    public FigCommandInfo[]? subcommands;
}

internal class ArrayOrSingleValueConverter<T> : JsonConverter<T[]?>
{
    public override T[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
            return JsonSerializer.Deserialize<T[]>(ref reader, options);

        var singleFigArg = JsonSerializer.Deserialize<T>(ref reader, options);
        return singleFigArg != null ? [singleFigArg] : [];
    }

    public override void Write(Utf8JsonWriter writer, T[]? value, JsonSerializerOptions options)
    {
        if (value?.Length == 1)
            JsonSerializer.Serialize(writer, value[0], options);
        else
            JsonSerializer.Serialize(writer, value, options);
    }
}

internal record FigOption
{
    [JsonInclude]
    [JsonConverter(typeof(ArrayOrSingleValueConverter<string>))]
    public string[] name = [];
    [JsonInclude]
    public string? description = null;
    [JsonInclude]
    public bool requiresSeparator = false;
    [JsonInclude]
    [JsonConverter(typeof(ArrayOrSingleValueConverter<FigArg>))]
    public FigArg[]? args;
}

internal record FigArg
{
    [JsonInclude]
    [JsonConverter(typeof(ArrayOrSingleValueConverter<string>))]
    public string[] name = [];
    [JsonInclude]
    public bool isOptional = false;
    [JsonInclude]
    [JsonConverter(typeof(ArrayOrSingleValueConverter<string>))]
    public string[]? template = null;
}

public class FigCommandInfoSupplier : ICommandInfoSupplier
{
    private readonly NPath _figBuildPath = Paths.FigAutoCompleteDir;
    private readonly NPath _figListPath = Paths.ShelperSupportFilesDir.Combine("list.js");
    public int Order => 1;

    private NPath CommandPath(string command) => _figBuildPath.Combine($"{command}.js");
    public bool CanHandle(string command) => CommandPath(command).FileExists();

    private static CommandInfo.ArgumentType GetArgumentType(FigOption figOption) => 
        figOption.name.All(n => n.StartsWith('-') && n.Length == 2) && !figOption.requiresSeparator
            ? CommandInfo.ArgumentType.Flag 
            : CommandInfo.ArgumentType.Keyword;

    private static CommandInfo.ArgumentType GetArgumentType(FigArg figArg)
    {
        if (figArg.template != null)
        {
            if (figArg.template.Any(t => t == "filepaths"))
                return CommandInfo.ArgumentType.FileSystemEntry;
            if (figArg.template.Any(t => t == "folders"))
                return CommandInfo.ArgumentType.Directory;
        }
        if (figArg.name.Any(n => n == "pathspec"))
            return CommandInfo.ArgumentType.FileSystemEntry;
        return CommandInfo.ArgumentType.String;
    }

    CommandInfo.Argument ConvertFigOption(FigOption figOption)
    {
        var type = GetArgumentType(figOption);
        return new(ConvertOptionName(figOption.name[0]))
        {
            Description = figOption.description ?? "",
            Aliases = figOption.name.Skip(1).Select(ConvertOptionName).ToArray(),
            Type = type,
            Arguments = [ figOption.args?.Select(ConvertFigArgument).ToArray() ?? [] ]
        };
        string ConvertOptionName(string name) => type == CommandInfo.ArgumentType.Flag ? name[1..] : name;
    }

    private CommandInfo.Argument ConvertFigArgument(FigArg figArg) => new (figArg.name.FirstOrDefault(GetArgumentType(figArg).ToString()))
    {
        Type = GetArgumentType(figArg),
        Optional = figArg.isOptional,
        Description = figArg.name.FirstOrDefault("")
    };

    private CommandInfo.Argument ConvertFigSubCommand(FigCommandInfo figCommand) => new (figCommand.name[0])
    {
        Type = CommandInfo.ArgumentType.Keyword,
        DontAllowMultiple = true,
        Aliases = figCommand.name.Skip(1).ToArray(),
        Description = figCommand.description ?? "",
        Arguments = ConvertFigArguments(figCommand)
    };

    private CommandInfo.Argument[][] ConvertFigArguments(FigCommandInfo figCommandInfo) =>
    [
        figCommandInfo.options?.Select(ConvertFigOption).ToArray() ?? [],
        figCommandInfo.args?.Select(ConvertFigArgument).ToArray() ?? [],
        figCommandInfo.subcommands?.Select(ConvertFigSubCommand).ToArray() ?? [],
    ];
    
    public async Task<CommandInfo?> GetCommandInfoForCommand(string command)
    {
        var commandResult = await Cli.Wrap("node")
            .WithArguments(new string[] {_figListPath.ToString(), CommandPath(command).ToString()})
            .ExecuteBufferedAsync();

        var json = commandResult.StandardOutput;
        var figCommandInfo = JsonSerializer.Deserialize<FigCommandInfo>(json);
        if (figCommandInfo == null)
            return null;
        var ci = new CommandInfo
        {
            Name = command,
            Description = figCommandInfo.description ?? "",
            Arguments = ConvertFigArguments(figCommandInfo),
            Comment = "This command info is generated from fig"
        };
        return ci;
    }
}