using System.Text.Json;
using System.Text.Json.Serialization;
using CliWrap;
using CliWrap.Buffered;
using NiceIO;

namespace boltprompt;

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

internal record FigGenerator
{
    [JsonInclude]
    [JsonConverter(typeof(ArrayOrSingleValueConverter<string>))]
    public string[]? script = [];

    [JsonInclude]
    [JsonConverter(typeof(ArrayOrSingleValueConverter<string>))]
    public string[]? extensions = [];

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
    [JsonInclude]
    [JsonConverter(typeof(ArrayOrSingleValueConverter<FigGenerator>))]
    public FigGenerator[]? generators = null;
}

public class FigCommandInfoSupplier : ICommandInfoSupplier
{
    private static readonly NPath FigAutoCompletePath = Environment.GetEnvironmentVariable("FIG_AUTOCOMPLETE_DIR") ?? "/dev/null";
    private static readonly NPath FigListDir = Paths.boltpromptSupportFilesDir.Combine("list-fig");
    private static readonly NPath FigListScript = FigListDir.Combine("list-fig-json.js");
    public int Order => 1;

    private NPath CommandPath(string command) => FigAutoCompletePath.Combine($"src/{command}.ts");
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

        if (figArg.generators != null)
        {
            if (figArg.generators[0].extensions?.Length > 0)
                return CommandInfo.ArgumentType.File;
            if (figArg.generators[0].script?.Length > 0)
                return CommandInfo.ArgumentType.CustomArgument;

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
            Arguments = figOption.args?.Select(ConvertFigArgument).ToArray() ?? []
        };
        string ConvertOptionName(string name) => type == CommandInfo.ArgumentType.Flag ? name[1..] : name;
    }

    private CommandInfo.ArgumentGroup ConvertFigArgument(FigArg figArg) => new (
            [new (figArg.name.FirstOrDefault(GetArgumentType(figArg).ToString())) {
                Type = GetArgumentType(figArg),
                Description = figArg.name.FirstOrDefault(""),
                Extensions = figArg.generators?[0].extensions
            }]
        )
    {
        Optional = figArg.isOptional,
    };

    private CommandInfo.Argument ConvertFigSubCommand(FigCommandInfo figCommand) => new (figCommand.name[0])
    {
        Type = CommandInfo.ArgumentType.Keyword,
        Aliases = figCommand.name.Skip(1).ToArray(),
        Description = figCommand.description ?? "",
        Arguments = ConvertFigArguments(figCommand)
    };

    private CommandInfo.ArgumentGroup[] ConvertFigArguments(FigCommandInfo figCommandInfo)
    {
        var arggroups = new List<CommandInfo.ArgumentGroup>();
        if (figCommandInfo.options != null)
            arggroups.Add(new(figCommandInfo.options.Select(ConvertFigOption).ToArray()) { Optional = true });
        if (figCommandInfo.args != null)
            arggroups.AddRange(figCommandInfo.args.Select(ConvertFigArgument));
        if (figCommandInfo.subcommands != null)
            arggroups.Add(new(figCommandInfo.subcommands.Select(ConvertFigSubCommand).ToArray()) { DontAllowMultiple = true });
        return arggroups.ToArray();
    }

    public async Task<CommandInfo?> GetCommandInfoForCommand(string command)
    {
        var tempArtifacts = NPath.CreateTempDirectory("fig-temp");
        Logger.Log("Fig", $"running tsc --outDir {tempArtifacts.ToString()} {CommandPath(command).ToString()}");

        var configPath = tempArtifacts.Combine("tsconfig.json");
        configPath.WriteAllText(
            $$"""
            {
              "compilerOptions": {
                "moduleResolution": "node",
                "target": "ES2018",
                "module": "ESNext",
                "lib": [
                  "ES2018",
                  "DOM"
                ],
                "noImplicitAny": false,
                "allowSyntheticDefaultImports": true,
                "baseUrl": "./",
                "types": [
                  "{{FigAutoCompletePath}}/node_modules/@withfig/autocomplete-types"
                ]
              },
              "exclude": [
                "node_modules/"
              ],
              "include": [
                "{{CommandPath(command)}}"
              ]
            }
            """);
        
        var commandResult = await Cli.Wrap("tsc")
            .WithArguments(new string[] { "--outDir", tempArtifacts.ToString(), "--project", configPath.ToString() })
            .WithWorkingDirectory(FigListDir.ToString())
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (commandResult.ExitCode != 0)
        {
            Logger.Log("Fig", $"Failed running tsc:\n{commandResult.StandardOutput}");
            return null;
        }

        Logger.Log("Fig",
            $"running node {FigListScript.ToString()} {tempArtifacts.Combine($"{command}.mjs").ToString()}");

        FigListDir.Combine("node_modules").Copy(tempArtifacts);
        tempArtifacts.Combine($"{command}.js").Move(tempArtifacts.Combine($"{command}.mjs"));
        commandResult = await Cli.Wrap("node")
            .WithArguments(new string[] { FigListScript.ToString(), tempArtifacts.Combine($"{command}.mjs").ToString() })
            .WithEnvironmentVariables(new Dictionary<string, string?> {{ "NODE_PATH", FigListDir.Combine("node_modules").ToString() }})
            .WithWorkingDirectory(FigListDir.ToString())
            .ExecuteBufferedAsync();

        if (commandResult.ExitCode != 0)
        {
            Logger.Log("Fig", $"Failed running node:\n{commandResult.StandardOutput}");
            return null;
        }

        var json = commandResult.StandardOutput;
        
        tempArtifacts.Delete();
        
        Logger.Log("Fig", $"Got json {json}");
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