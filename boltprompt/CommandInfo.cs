using System.Text.Json;
using System.Text.Json.Serialization;
using LanguageModels;

namespace boltprompt;

public record CommandInfo
{
    private static JsonSerializerOptions _serializerOptions = new () 
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Serialize() => JsonSerializer.Serialize(this, _serializerOptions);

    public static CommandInfo? Deserialize(string json) => JsonSerializer.Deserialize<CommandInfo>(json, _serializerOptions);
    
    [JsonInclude]
    public string? Comment;
    [JsonInclude]
    [DescriptionForLanguageModel("The name of the command")]
    public string Name = "";
    [JsonInclude]
    [DescriptionForLanguageModel("A short description what the command does")]
    public string Description = "";
    [JsonInclude]
    [DescriptionForLanguageModel("A list of Argument groups with command line arguments accepted by the command. All arguments in a single group can come in any random order. If an argument needs to come after another argument, it should come in a new group. If the command does not take any arguments, this should be empty.")]
    public ArgumentGroup[]? Arguments;
    [JsonInclude]
    [DescriptionForLanguageModel("A list of custom argument templates used to define the commands to run for custom arguments. If no arguments of type 'customargument' are used, this can be empty or null.")]
    public CustomArgumentTemplate[]? CustomArgumentTemplates;

    public record CustomArgumentTemplate
    {
        [JsonInclude]
        [DescriptionForLanguageModel("If this is enabled, arguments will only be accepted if they match the output of the custom command. If not, the custom command will only be used to obtain suggestions, but any argument will be matched.")]
        public bool StrictMatching = false;
        [JsonInclude]
        [DescriptionForLanguageModel("The name of the template. This must match the 'customargument' field in arguments using this template.")]
        public string Name = "";
        [JsonInclude]
        [DescriptionForLanguageModel("The command to run to get suggestions.")]
        public string Command = "";
        [JsonInclude]
        [DescriptionForLanguageModel("A Regex to match the output of the custom command to suggestions and descriptions.")]
        public string? Regex;
    }
    
    public enum ArgumentType
    {
        Keyword,
        Flag,
        FileSystemEntry,
        Directory,
        File,
        Command,
        CommandName,
        CustomArgument,
        String,
        Unknown // Potentially a file system entry
    }
    
    public record ArgumentGroup([DescriptionForLanguageModel("Arguments belonging to this argument group")]Argument[] Arguments)
    {
        [JsonInclude]
        [DescriptionForLanguageModel("Does the command require an argument from this group to be on the command line?")]
        public bool Optional;
        [JsonInclude]
        [DescriptionForLanguageModel("If true, only one argument from this group is allowed. If false, there can be multiple.")]
        public bool DontAllowMultiple;
    }
    
    public record Argument([DescriptionForLanguageModel("The name of the argument. If the argument is of type `flag`, this is the single-character flag to enable the argument. If the argument is of type `keyword`, this is the string passed to the command line for this argument.")]string Name)
    {
        [JsonInclude]
        [DescriptionForLanguageModel("If true, this argument can be on the command line multiple times. If false, only once.")]
        public bool Repeat;
        [JsonInclude]
        [DescriptionForLanguageModel("A short description what the argument does")]
        public string Description = "";
        [JsonInclude]
        [DescriptionForLanguageModel(
            """
            Type of the argument. Must be one of:
             'Keyword': Use this when the argument is invoked by a specific word on the command line, like e.g. '--help' . In this case, 'name' should be the word used on the command line, e.g. '--help'.
             'Flag': Like 'Keyword', but for single-character flags, like '-h'. In this case 'name' should be a single character without a dash, e.h. 'h'.
             'FileSystemEntry': The user can pass any file system path as this argument.
             'File': The user can pass any file system path pointing to a file as this argument.
             'Directory': The user can pass any file system path pointing to a directory as this argument.
             'Command': The argument is a command line command itself (like the argument to 'sudo').
             'CommandName': The argument is the name of another command line executable.
             'CustomArgument': Possible argument values are determined by running a command. 'customargumenttemplate' is must match the name of a custom argument template defining the command to run.
             'String': The argument can be any arbitrary string. This matches anything.
            """)]
        public ArgumentType Type = ArgumentType.Keyword;
        [JsonInclude]
        [DescriptionForLanguageModel("Valid file extensions (only applicable if type is 'file').")]
        public string[]? Extensions;
        [JsonInclude]
        [DescriptionForLanguageModel("The name of a custom argument template defining the command to run (only applicable if type is 'customargument').")]
        public string? CustomArgumentTemplate;
        [JsonInclude]
        [DescriptionForLanguageModel("Alternative names for the argument (if any).")]
        public string[]? Aliases;
        [JsonInclude]
        [DescriptionForLanguageModel("A list of Argument groups with command line sub arguments following the argument. All arguments in a single group can come in any random order. If an argument needs to come after another argument, it should come in a new group. If the argument does not take any sub arguments, this should be empty.")]
        public ArgumentGroup[]? Arguments;

        [JsonIgnore]
        public string[] AllNames => new[] { Name }.Concat(Aliases ?? []).ToArray(); 
        public static implicit operator Argument(string name) => new(name);
    }

    public static CommandInfo DefaultCommand { get; } = new()
    {
        Arguments = 
        [new ([
            new ("") {Type = ArgumentType.Unknown, Repeat = true }
        ])]
    };

    public static CommandInfo Ls { get; } = new ()
    {
        Name = "ls",
        Description = "list directory contents",
        Arguments =
        [
            new ([
                new ("l") { Description = "List in long format. Ownership, Date/Time etc (See below) For terminal output, a total sum of all the file sizes is  output on a line before the long listing. If the file is a symbolic link the pathname of the linked-to file is preceded by ->", Repeat = true, Type = ArgumentType.Flag},
                new ("a") { Description = "List all entries including those starting with a dot .", Repeat = true, Type = ArgumentType.Flag},
                new ("h") { Description = "When used with the -l option, use unit suffixes: Byte, Kilobyte, Megabyte, Gigabyte, Terabyte and Petabyte in order to reduce the number of digits to three or less using base 2 for sizes.", Repeat = true, Type = ArgumentType.Flag},
                new ("R") { Description = "Recursively list subdirectories encountered.", Repeat = true, Type = ArgumentType.Flag},
                new ("t") { Description = "Sort by time modified (most recently modified first) before sorting the operands by lexicographical order.", Repeat = true, Type = ArgumentType.Flag},
                new ("r") { Description = "Reverse the order of the sort to get reverse lexicographical order or the oldest entries first. (or largest files last, if combined with sort by size)", Repeat = true, Type = ArgumentType.Flag},
                new ("S") { Description = "Sort files by size", Repeat = true, Type = ArgumentType.Flag},
                new ("d") { Description = "Directories are listed as plain files (not searched recursively).", Repeat = true, Type = ArgumentType.Flag},
                new ("1") { Description = "(The numeric digit 'one'.)  Force output to be one entry per line.  This is the default when output is not to a terminal.", Repeat = true, Type = ArgumentType.Flag},
                new ("F") { Description = "Display a slash / immediately after each pathname that is a directory, an asterisk * after each that is executable, an at sign @ after each symbolic link, an equals sign = after each socket, a percent sign % after each whiteout, and a vertical bar | after each that is a FIFO.", Repeat = true, Type = ArgumentType.Flag},
                new ("i") { Description = "For each file, print the file's file serial number (inode number).", Repeat = true, Type = ArgumentType.Flag},
                new ("G") { Description = "Enable colour output. This option is equivalent to defining CLICOLOR or COLORTERM in the environment and setting --color=auto. (See below.)", Repeat = true, Type = ArgumentType.Flag},
                new ("@") { Description = "Display extended attribute keys and sizes.", Repeat = true, Type = ArgumentType.Flag},
                new ("A") { Description = "List all entries including those starting with a dot . Except for . and .. This option is always set for the superuser (via sudo).", Repeat = true, Type = ArgumentType.Flag},
                new ("B") { Description = "Force printing of non-printable characters (as defined by ctype(3) and current locale settings) in file names as xxx, where xxx is the numeric value of the character in octal.", Repeat = true, Type = ArgumentType.Flag},
                new ("b") { Description = "As -B, but use C escape codes whenever possible.", Repeat = true, Type = ArgumentType.Flag},
                new ("C") { Description = "Force multi-column output; this is the default when output is to a terminal.", Repeat = true, Type = ArgumentType.Flag},
                new ("c") { Description = "Use time when file status was last changed for sorting or printing.", Repeat = true, Type = ArgumentType.Flag},
                new ("f") { Description = "Output is not sorted.", Repeat = true, Type = ArgumentType.Flag},
                new ("g") { Description = "This option is deprecated. This option is only available for compatibility with POSIX; it is used to display the group name in the long (-l) format output (the owner name is suppressed).", Repeat = true, Type = ArgumentType.Flag},
                new ("H") { Description = "Symbolic links on the command line are followed. This option is assumed if none of the -F, -d, or -l options are specified.", Repeat = true, Type = ArgumentType.Flag},
                new ("k") { Description = "If the -s option is specified, print the file size allocation in kilobytes, not blocks.  This option overrides the environment variable BLOCKSIZE.", Repeat = true, Type = ArgumentType.Flag},
                new ("L") { Description = "If argument is a symbolic link, list the file or directory the link references rather than the link itself.  This option cancels the -P option.", Repeat = true, Type = ArgumentType.Flag},
                new ("m") { Description = "Stream output format; list files across the page, separated by commas.", Repeat = true, Type = ArgumentType.Flag},
                new ("n") { Description = "Display user and group IDs numerically rather than converting to a user or group name in a long (-l) output. This option turns on the -l option.", Repeat = true, Type = ArgumentType.Flag},
                new ("O") { Description = "Include the file flags in a long (-l) output.", Repeat = true, Type = ArgumentType.Flag},
                new ("o") { Description = "List in long format, but omit the group id.", Repeat = true, Type = ArgumentType.Flag},
                new ("P") { Description = "If argument is a symbolic link, list the link itself rather than the object the link references.  This option cancels the -H and -L options.", Repeat = true, Type = ArgumentType.Flag},
                new ("p") { Description = "Write a slash (/) after each filename if that file is a directory.", Repeat = true, Type = ArgumentType.Flag},
                new ("q") { Description = "Force printing of non-graphic characters in file names as the character '?'; this is the default when output is to a terminal.", Repeat = true, Type = ArgumentType.Flag},
                new ("s") { Description = "Display the number of file system blocks actually used by each file, in units of 512 bytes, where partial units are rounded up to the next integer value.   If the output is to a terminal, a total sum for all the file sizes is output on a line before the listing.  The environment variable BLOCKSIZE overrides the unit size of 512 bytes.", Repeat = true, Type = ArgumentType.Flag},
                new ("T") { Description = "When used with the -l (lowercase letter 'ell') option, display complete time information for the file, including month, day, hour, minute, second, and year.", Repeat = true, Type = ArgumentType.Flag},
                new ("u") { Description = "Use time of last access, instead of last modification of the file for sorting (-t) or printing (-l).", Repeat = true, Type = ArgumentType.Flag},
                new ("v") { Description = "Force unedited printing of non-graphic characters. This is the default when output is not to a terminal.", Repeat = true, Type = ArgumentType.Flag},
                new ("W") { Description = "Display whiteouts when scanning directories. (-S) flag).", Repeat = true, Type = ArgumentType.Flag},
                new ("w") { Description = "Force raw printing of non-printable characters.  This is the default when output is not to a terminal.", Repeat = true, Type = ArgumentType.Flag},
                new ("x") { Description = "The same as -C, except that the multi-column output is produced with entries sorted across, rather than down, the columns.", Repeat = true, Type = ArgumentType.Flag},
            ]) { Optional = true },
            new ([new ("FileSystemEntry") { Repeat = true, Type = ArgumentType.FileSystemEntry, Description = "A directory for which ls should list contents or a file for which ls should list information as requested."}])
        ]
    };
}