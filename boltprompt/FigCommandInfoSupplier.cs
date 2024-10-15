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
    public string? loadSpec;
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

internal class SuggestionConverter : JsonConverter<FigSuggestion[]>
{
    FigSuggestion? ReadSuggestion(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                return JsonSerializer.Deserialize<FigSuggestion>(ref reader, options);
            case JsonTokenType.String:
            {
                var name = reader.GetString();
                if (name != null)
                    return new () { name = [name] };
                return null;
            }
            default:
                throw new JsonException("Invalid JSON format for FigSuggestion array.");
        }
    }
    
    public override FigSuggestion[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var suggestions = new List<FigSuggestion>();
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    break;
                var sug = ReadSuggestion(ref reader, options);
                if (sug != null)
                    suggestions.Add(sug);
            }
        }
        else
        {
            var sug = ReadSuggestion(ref reader, options);
            if (sug != null)
                suggestions.Add(sug);
        }

        return suggestions.ToArray();


    }

    public override void Write(Utf8JsonWriter writer, FigSuggestion[] value, JsonSerializerOptions options)
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
    public string?[] name = [];
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

    [JsonInclude]
    public string? showFolders;
}

internal record FigSuggestion
{
    [JsonInclude]
    [JsonConverter(typeof(ArrayOrSingleValueConverter<string>))]
    public string[] name = [];
    [JsonInclude]
    public string? description;
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
    [JsonConverter(typeof(SuggestionConverter))]
    public FigSuggestion[]? suggestions = null;
    [JsonInclude]
    [JsonConverter(typeof(ArrayOrSingleValueConverter<FigGenerator>))]
    public FigGenerator[]? generators = null;
}

internal class FigCommandInfoSupplier : ICommandInfoSupplier
{
    private static readonly NPath FigAutoCompletePath = Environment.GetEnvironmentVariable("FIG_AUTOCOMPLETE_DIR") ?? "/dev/null";
    private static readonly NPath FigListDir = Paths.boltpromptSupportFilesDir.Combine("list-fig");
    private static readonly NPath FigListScript = FigListDir.Combine("list-fig-json.js");
    private static readonly NPath FigAutoCompleteSrcPath = FigAutoCompletePath.Combine("src");
    public int Order => 1;

    private List<CommandInfo.CustomArgumentTemplate> _customArgumentTemplates = new();

    private NPath CommandPath(string command) => FigAutoCompleteSrcPath.Combine($"{command}.ts");
    public bool CanHandle(string command) => CommandPath(command).FileExists();

    public async Task ConvertAll(NPath outputDir)
    {
        outputDir.CreateDirectory();
        foreach (var file in FigAutoCompleteSrcPath.Files(new string[] {"ts"}))
        {                
            var commandName = file.FileNameWithoutExtension;
            try
            {
                Console.WriteLine($"Converting {commandName}...");
                var ci = await GetCommandInfoForCommand(commandName);
                if (ci != null)
                {
                    var outPath = outputDir.Combine($"{commandName}.json");
                    Console.WriteLine($"Writing {outPath}...");
                    outPath.WriteAllText(ci.Serialize());
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed converting {commandName}:\n{ex}");
            }
        }
    }


    private static CommandInfo.ArgumentType GetArgumentType(FigOption figOption) => 
        figOption.name.All(n => n != null && n.StartsWith('-') && n.Length == 2) && !figOption.requiresSeparator
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
            if (figArg.generators[0].showFolders != null)
                return CommandInfo.ArgumentType.Directory;
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
        return new(ConvertOptionName(figOption.name[0] ?? ""))
        {
            Description = figOption.description ?? "",
            Aliases = figOption.name.Skip(1).Where(n => n != null).Select(ConvertOptionName).ToArray(),
            Type = type,
            Arguments = figOption.args?.Select(ConvertFigArgument).ToArray() ?? []
        };
        string ConvertOptionName(string? name) => name != null ? type == CommandInfo.ArgumentType.Flag ? name[1..] : name : "";
    }

    private string? GetCustomCommandTemplateForGenerator(FigArg figArg)
    {
        var script = figArg.generators?[0].script != null ? string.Join(" ", figArg.generators?[0].script!) : null;
        if (string.IsNullOrEmpty(script))
            return null;
        var foundTemplate = _customArgumentTemplates.FirstOrDefault(t => t.Command == script);
        if (foundTemplate != null)
            return foundTemplate.Name;

        var baseName = figArg.name.FirstOrDefault(GetArgumentType(figArg).ToString());
        var name = baseName;
        var num = 0;
        while (_customArgumentTemplates.Any(t => t.Name == name))
            name = $"{baseName}{num++}";

        _customArgumentTemplates.Add(new ()
        {
            Name = name,
            Command = script
        });
        return name;
    }
    
    private CommandInfo.ArgumentGroup ConvertFigArgument(FigArg figArg) => new (
        new []
            {new CommandInfo.Argument(figArg.name.FirstOrDefault(GetArgumentType(figArg).ToString())) {
                Type = GetArgumentType(figArg),
                Description = figArg.name.FirstOrDefault(""),
                Extensions = figArg.generators?[0].extensions ?? null,
                CustomArgumentTemplate = GetCustomCommandTemplateForGenerator(figArg)
            }}
            .Concat(figArg.suggestions?.Select(s => new CommandInfo.Argument(s.name.FirstOrDefault("")) { Description = s.description ?? figArg.name.FirstOrDefault() ?? ""}) ?? [])
            .ToArray()
        )
    {
        Optional = figArg.isOptional,
    };

    private async Task<CommandInfo.Argument> ConvertFigSubCommand(FigCommandInfo figCommand)
    {
        if (!string.IsNullOrEmpty(figCommand.loadSpec))
        {
            var childCommandInfo = await LoadFigCommandInfo(figCommand.loadSpec);
            if (childCommandInfo != null)
                return await ConvertFigSubCommand(childCommandInfo);
        }

        return new(figCommand.name[0])
        {
            Type = CommandInfo.ArgumentType.Keyword,
            Aliases = figCommand.name.Skip(1).ToArray(),
            Description = figCommand.description ?? "",
            Arguments = await ConvertFigArguments(figCommand)
        };
    }

    private async Task<CommandInfo.ArgumentGroup[]> ConvertFigArguments(FigCommandInfo figCommandInfo)
    {
        var arggroups = new List<CommandInfo.ArgumentGroup>();
        if (figCommandInfo.options != null)
            arggroups.Add(new(figCommandInfo.options.Select(ConvertFigOption).ToArray()) { Optional = true });
        if (figCommandInfo.args != null)
            arggroups.AddRange(figCommandInfo.args.Select(ConvertFigArgument));
        if (figCommandInfo.subcommands != null)
            arggroups.Add(new((await Task.WhenAll(figCommandInfo.subcommands.Select(ConvertFigSubCommand))).ToArray()) { DontAllowMultiple = true });
        return arggroups.ToArray();
    }

    public async Task<CommandInfo?> GetCommandInfoForCommand(string command)
    {
        var figCommandInfo = await LoadFigCommandInfo(command);
        if (figCommandInfo == null)
            return null;
        var ci = new CommandInfo
        {
            Name = command,
            Description = figCommandInfo.description ?? "",
            Arguments = await ConvertFigArguments(figCommandInfo),
            Comment = "This command info is generated from fig",
            CustomArgumentTemplates = _customArgumentTemplates.Count > 0 ? _customArgumentTemplates.ToArray() : null
        };
        _customArgumentTemplates.Clear();
        return ci;
    }

    private async Task<FigCommandInfo?> LoadFigCommandInfo(string command, NPath? dir = null)
    {
        var tempArtifacts = dir ?? NPath.CreateTempDirectory("fig-temp");
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
        tempArtifacts.Combine("package.json").WriteAllText(
            """
            {
              "dependencies": {
                "semver": "^7.6.2",
                "strip-json-comments": "^5.0.1",
                "yaml": "^2.4.5",
                "@fig/autocomplete-generators": "1.0.0"
              }
            }
            
            """);
        
        commandResult = await Cli.Wrap("npm")
            .WithArguments("install")
            .WithWorkingDirectory(tempArtifacts.ToString())
            .ExecuteBufferedAsync();
        
        if (commandResult.ExitCode != 0)
        {
            Logger.Log("Fig", $"Failed running npm install in {tempArtifacts}");
            return null;
        }
        
        command = new NPath(command).FileName;
        var targetPath = tempArtifacts.Combine($"{command}.mjs");
        if (targetPath.FileExists())
            targetPath.Delete();
        tempArtifacts.Combine($"{command}.js").Move(targetPath);
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

        if (!string.IsNullOrEmpty(commandResult.StandardError))
        {
            Logger.Log("Fig", $"node errors:\n{commandResult.StandardError}");
            if (commandResult.StandardError.Contains("Cannot find module"))
            {
                var start = commandResult.StandardError.IndexOf("Cannot find module");
                start = commandResult.StandardError.IndexOf("'", start) + 1;
                var end = commandResult.StandardError.IndexOf("'", start);
                var moduleName = new NPath(commandResult.StandardError[start..end]).FileName;
                Console.WriteLine($"Importing submodule {moduleName}");
                if (await LoadFigCommandInfo(moduleName, tempArtifacts) != null)
                {
                    tempArtifacts.Combine($"{moduleName}.mjs").Copy(tempArtifacts.Combine(moduleName));
                    return await LoadFigCommandInfo(command, tempArtifacts);
                }
            }
        }

        var json = commandResult.StandardOutput; 
        
        if (dir is null)
            tempArtifacts.Delete();
        
        Logger.Log("Fig", $"Got json\n{json}");
        return JsonSerializer.Deserialize<FigCommandInfo>(json);
    }
}