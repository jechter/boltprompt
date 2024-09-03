using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shelper;

public record CommandInfo
{
    private static JsonSerializerOptions SerializerOptions = new () 
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    public static CommandInfo? Deserialize(string json) => JsonSerializer.Deserialize<CommandInfo>(json, SerializerOptions);
    
    [JsonInclude]
    public string Comment;
    [JsonInclude]
    public string Name;
    [JsonInclude]
    public string Description;
    [JsonInclude]
    public Argument[][]? Arguments;
    
    public enum ArgumentType
    {
        Keyword,
        Flag,
        FileSystemEntry,
        Directory,
        File,
        Command,
        String,
    }
    

    public record Argument(string Name)
    {
        [JsonInclude]
        public bool Optional = true;
        [JsonInclude]
        public bool Repeat = false;
        [JsonInclude]
        public string Description;
        [JsonInclude]
        public ArgumentType Type = ArgumentType.Keyword;
        [JsonInclude]
        public string[]? Aliases;
        [JsonInclude]
        public Argument[]? Arguments;

        [JsonIgnore]
        public string[] AllNames => new[] { Name }.Concat(Aliases ?? []).ToArray(); 
        public static implicit operator Argument(string name) => new(name);
    }


    public static CommandInfo ls = new CommandInfo
    {
        Name = "ls",
        Description = "list directory contents",
        Arguments =
        [
            [
                new ("@") { Description = "Display extended attribute keys and sizes.", Repeat = true, Type = ArgumentType.Flag},
                new ("1") { Description = "(The numeric digit 'one'.)  Force output to be one entry per line.  This is the default when output is not to a terminal.", Repeat = true, Type = ArgumentType.Flag},
                new ("A") { Description = "List all entries including those starting with a dot . Except for . and .. This option is always set for the superuser (via sudo).", Repeat = true, Type = ArgumentType.Flag},
                new ("a") { Description = "List all entries including those starting with a dot .", Repeat = true, Type = ArgumentType.Flag},
                new ("B") { Description = "Force printing of non-printable characters (as defined by ctype(3) and current locale settings) in file names as xxx, where xxx is the numeric value of the character in octal.", Repeat = true, Type = ArgumentType.Flag},
                new ("b") { Description = "As -B, but use C escape codes whenever possible.", Repeat = true, Type = ArgumentType.Flag},
                new ("C") { Description = "Force multi-column output; this is the default when output is to a terminal.", Repeat = true, Type = ArgumentType.Flag},
                new ("c") { Description = "Use time when file status was last changed for sorting or printing.", Repeat = true, Type = ArgumentType.Flag},
                new ("d") { Description = "Directories are listed as plain files (not searched recursively).", Repeat = true, Type = ArgumentType.Flag},
                new ("F") { Description = "Display a slash / immediately after each pathname that is a directory, an asterisk * after each that is executable, an at sign @ after each symbolic link, an equals sign = after each socket, a percent sign % after each whiteout, and a vertical bar | after each that is a FIFO.", Repeat = true, Type = ArgumentType.Flag},
                new ("f") { Description = "Output is not sorted.", Repeat = true, Type = ArgumentType.Flag},
                new ("G") { Description = "Enable colour output. This option is equivalent to defining CLICOLOR or COLORTERM in the environment and setting --color=auto. (See below.)", Repeat = true, Type = ArgumentType.Flag},
                new ("g") { Description = "This option is deprecated. This option is only available for compatibility with POSIX; it is used to display the group name in the long (-l) format output (the owner name is suppressed).", Repeat = true, Type = ArgumentType.Flag},
                new ("H") { Description = "Symbolic links on the command line are followed. This option is assumed if none of the -F, -d, or -l options are specified.", Repeat = true, Type = ArgumentType.Flag},
                new ("h") { Description = "When used with the -l option, use unit suffixes: Byte, Kilobyte, Megabyte, Gigabyte, Terabyte and Petabyte in order to reduce the number of digits to three or less using base 2 for sizes.", Repeat = true, Type = ArgumentType.Flag},
                new ("i") { Description = "For each file, print the file's file serial number (inode number).", Repeat = true, Type = ArgumentType.Flag},
                new ("k") { Description = "If the -s option is specified, print the file size allocation in kilobytes, not blocks.  This option overrides the environment variable BLOCKSIZE.", Repeat = true, Type = ArgumentType.Flag},
                new ("L") { Description = "If argument is a symbolic link, list the file or directory the link references rather than the link itself.  This option cancels the -P option.", Repeat = true, Type = ArgumentType.Flag},
                new ("l") { Description = "List in long format. Ownership, Date/Time etc (See below) For terminal output, a total sum of all the file sizes is  output on a line before the long listing. If the file is a symbolic link the pathname of the linked-to file is preceded by ->", Repeat = true, Type = ArgumentType.Flag},
                new ("m") { Description = "Stream output format; list files across the page, separated by commas.", Repeat = true, Type = ArgumentType.Flag},
                new ("n") { Description = "Display user and group IDs numerically rather than converting to a user or group name in a long (-l) output. This option turns on the -l option.", Repeat = true, Type = ArgumentType.Flag},
                new ("O") { Description = "Include the file flags in a long (-l) output.", Repeat = true, Type = ArgumentType.Flag},
                new ("o") { Description = "List in long format, but omit the group id.", Repeat = true, Type = ArgumentType.Flag},
                new ("P") { Description = "If argument is a symbolic link, list the link itself rather than the object the link references.  This option cancels the -H and -L options.", Repeat = true, Type = ArgumentType.Flag},
                new ("p") { Description = "Write a slash (/) after each filename if that file is a directory.", Repeat = true, Type = ArgumentType.Flag},
                new ("q") { Description = "Force printing of non-graphic characters in file names as the character '?'; this is the default when output is to a terminal.", Repeat = true, Type = ArgumentType.Flag},
                new ("R") { Description = "Recursively list subdirectories encountered.", Repeat = true, Type = ArgumentType.Flag},
                new ("r") { Description = "Reverse the order of the sort to get reverse lexicographical order or the oldest entries first. (or largest files last, if combined with sort by size)", Repeat = true, Type = ArgumentType.Flag},
                new ("S") { Description = "Sort files by size", Repeat = true, Type = ArgumentType.Flag},
                new ("s") { Description = "Display the number of file system blocks actually used by each file, in units of 512 bytes, where partial units are rounded up to the next integer value.   If the output is to a terminal, a total sum for all the file sizes is output on a line before the listing.  The environment variable BLOCKSIZE overrides the unit size of 512 bytes.", Repeat = true, Type = ArgumentType.Flag},
                new ("T") { Description = "When used with the -l (lowercase letter 'ell') option, display complete time information for the file, including month, day, hour, minute, second, and year.", Repeat = true, Type = ArgumentType.Flag},
                new ("t") { Description = "Sort by time modified (most recently modified first) before sorting the operands by lexicographical order.", Repeat = true, Type = ArgumentType.Flag},
                new ("u") { Description = "Use time of last access, instead of last modification of the file for sorting (-t) or printing (-l).", Repeat = true, Type = ArgumentType.Flag},
                new ("v") { Description = "Force unedited printing of non-graphic characters. This is the default when output is not to a terminal.", Repeat = true, Type = ArgumentType.Flag},
                new ("W") { Description = "Display whiteouts when scanning directories. (-S) flag).", Repeat = true, Type = ArgumentType.Flag},
                new ("w") { Description = "Force raw printing of non-printable characters.  This is the default when output is not to a terminal.", Repeat = true, Type = ArgumentType.Flag},
                new ("x") { Description = "The same as -C, except that the multi-column output is produced with entries sorted across, rather than down, the columns.", Repeat = true, Type = ArgumentType.Flag},
            ],
            [new ("FileSystemEntry") { Repeat = true, Type = ArgumentType.FileSystemEntry }]
        ]
    };
}
/*
public record CommandInfo
{
    private static JsonSerializerOptions SerializerOptions = new () 
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    public static CommandInfo? Deserialize(string json) => JsonSerializer.Deserialize<CommandInfo>(json, SerializerOptions);
    
    [JsonInclude]
    public string Comment;
    [JsonInclude]
    public string Name;
    [JsonInclude]
    public string Description;
    [JsonInclude]
    public Argument[]? Arguments;
    [JsonInclude]
    public ArgumentDescription[]? ArgumentDescriptions;
    [JsonInclude]
    public ArgumentGroup[]? ArgumentGroups;

    public enum ArgumentType
    {
        Keyword,
        FileSystemEntry,
        Directory,
        File,
        Command,
        String,
    }
    
    public record ArgumentDescription
    {
        [JsonInclude]
        public string Name;
        [JsonInclude]
        public string Description;
        [JsonInclude]
        public ArgumentType Type = ArgumentType.Keyword;
        [JsonInclude]
        public string[]? Values;
        [JsonInclude]
        public Argument[]? Arguments;
    }

    public record Argument(string Name)
    {
        [JsonInclude]
        public bool Optional = true;
        [JsonInclude]
        public bool Repeat = false;
        public static implicit operator Argument(string name) => new(name);
    }
    
    public record ArgumentGroup
    {
        [JsonInclude]
        public string Name;
        [JsonInclude]
        public Argument[] Arguments;
    }

    public static CommandInfo ls = new CommandInfo()
    {
        Name = "ls",
        Description = "list directory contents",
        Arguments = [new ("Flags") {Repeat = true}, new Argument("FileSystemEntry") {Repeat = true}], 
        ArgumentDescriptions = [
            new ArgumentDescription
            {
                Name = "FileSystemEntry",
                Description = "File or Directory to list",
                Type = ArgumentType.FileSystemEntry,
            },
            new ArgumentDescription {
                Name = "Flags",
                Description = "Flags",
                Values = ["-"],
                Arguments = [new Argument("Flag")
                {
                    Optional = false,
                    Repeat = true,
                }]
            },
            new ArgumentDescription {
                Name = "@",
                Description = "Display extended attribute keys and sizes.",
                Values = ["@"]
            },
            new ArgumentDescription {
                Name = "1",
                Description = "(The numeric digit 'one'.)  Force output to be one entry per line.  This is the default when output is not to a terminal.",
                Values = ["1"]
            },
            new ArgumentDescription {
                Name = "A",
                Description = "List all entries including those starting with a dot . Except for . and .. This option is always set for the superuser (via sudo).",
                Values = ["A"]
            },
            new ArgumentDescription {
                Name = "a",
                Description = "List all entries including those starting with a dot .",
                Values = ["a"]
            },
            new ArgumentDescription {
                Name = "B",
                Description = "Force printing of non-printable characters (as defined by ctype(3) and current locale settings) in file names as xxx, where xxx is the numeric value of the character in octal.",
                Values = ["B"]
            },
            new ArgumentDescription {
                Name = "b",
                Description = "As -B, but use C escape codes whenever possible.",
                Values = ["b"]
            },
            new ArgumentDescription {
                Name = "C",
                Description = "Force multi-column output; this is the default when output is to a terminal.",
                Values = ["C"]
            },
            new ArgumentDescription {
                Name = "c",
                Description = "Use time when file status was last changed for sorting or printing.",
                Values = ["c"]
            },
            new ArgumentDescription {
                Name = "d",
                Description = "Directories are listed as plain files (not searched recursively).",
                Values = ["d"]
            },
            new ArgumentDescription {
                Name = "F",
                Description = "Display a slash / immediately after each pathname that is a directory, an asterisk * after each that is executable, an at sign @ after each symbolic link, an equals sign = after each socket, a percent sign % after each whiteout, and a vertical bar | after each that is a FIFO.",
                Values = ["F"]
            },
            new ArgumentDescription {
                Name = "f",
                Description = "Output is not sorted.",
                Values = ["f"]
            },
            new ArgumentDescription {
                Name = "G",
                Description = "Enable colour output. This option is equivalent to defining CLICOLOR or COLORTERM in the environment and setting --color=auto. (See below.)",
                Values = ["G"]
            },
            new ArgumentDescription {
                Name = "g",
                Description = "This option is deprecated. This option is only available for compatibility with POSIX; it is used to display the group name in the long (-l) format output (the owner name is suppressed).",
                Values = ["g"]
            },
            new ArgumentDescription {
                Name = "H",
                Description = "Symbolic links on the command line are followed. This option is assumed if none of the -F, -d, or -l options are specified.",
                Values = ["H"]
            },
            new ArgumentDescription {
                Name = "h",
                Description = "When used with the -l option, use unit suffixes: Byte, Kilobyte, Megabyte, Gigabyte, Terabyte and Petabyte in order to reduce the number of digits to three or less using base 2 for sizes.",
                Values = ["h"]
            },
            new ArgumentDescription {
                Name = "i",
                Description = "For each file, print the file's file serial number (inode number).",
                Values = ["i"]
            },
            new ArgumentDescription {
                Name = "k",
                Description = "If the -s option is specified, print the file size allocation in kilobytes, not blocks.  This option overrides the environment variable BLOCKSIZE.",
                Values = ["k"]
            },
            new ArgumentDescription {
                Name = "L",
                Description = "If argument is a symbolic link, list the file or directory the link references rather than the link itself.  This option cancels the -P option.",
                Values = ["L"]
            },
            new ArgumentDescription {
                Name = "l",
                Description = "List in long format. Ownership, Date/Time etc (See below) For terminal output, a total sum of all the file sizes is  output on a line before the long listing. If the file is a symbolic link the pathname of the linked-to file is preceded by ->",
                Values = ["l"]
            },
            new ArgumentDescription {
                Name = "m",
                Description = "Stream output format; list files across the page, separated by commas.",
                Values = ["m"]
            },
            new ArgumentDescription {
                Name = "n",
                Description = "Display user and group IDs numerically rather than converting to a user or group name in a long (-l) output. This option turns on the -l option.",
                Values = ["n"]
            },
            new ArgumentDescription {
                Name = "O",
                Description = "Include the file flags in a long (-l) output.",
                Values = ["O"]
            },
            new ArgumentDescription {
                Name = "o",
                Description = "List in long format, but omit the group id.",
                Values = ["o"]
            },
            new ArgumentDescription {
                Name = "P",
                Description = "If argument is a symbolic link, list the link itself rather than the object the link references.  This option cancels the -H and -L options.",
                Values = ["P"]
            },
            new ArgumentDescription {
                Name = "p",
                Description = "Write a slash (/) after each filename if that file is a directory.",
                Values = ["p"]
            },
            new ArgumentDescription {
                Name = "q",
                Description = "Force printing of non-graphic characters in file names as the character '?'; this is the default when output is to a terminal.",
                Values = ["q"]
            },
            new ArgumentDescription {
                Name = "R",
                Description = "Recursively list subdirectories encountered.",
                Values = ["R"]
            },
            new ArgumentDescription {
                Name = "r",
                Description = "Reverse the order of the sort to get reverse lexicographical order or the oldest entries first. (or largest files last, if combined with sort by size)",
                Values = ["r"]
            },
            new ArgumentDescription {
                Name = "S",
                Description = "Sort files by size",
                Values = ["S"]
            },
            new ArgumentDescription {
                Name = "s",
                Description = "Display the number of file system blocks actually used by each file, in units of 512 bytes, where partial units are rounded up to the next integer value.   If the output is to a terminal, a total sum for all the file sizes is output on a line before the listing.  The environment variable BLOCKSIZE overrides the unit size of 512 bytes.",
                Values = ["s"]
            },
            new ArgumentDescription {
                Name = "T",
                Description = "When used with the -l (lowercase letter 'ell') option, display complete time information for the file, including month, day, hour, minute, second, and year.",
                Values = ["T"]
            },
            new ArgumentDescription {
                Name = "t",
                Description = "Sort by time modified (most recently modified first) before sorting the operands by lexicographical order.",
                Values = ["t"]
            },
            new ArgumentDescription {
                Name = "u",
                Description = "Use time of last access, instead of last modification of the file for sorting (-t) or printing (-l).",
                Values = ["u"]
            },
            new ArgumentDescription {
                Name = "v",
                Description = "Force unedited printing of non-graphic characters. This is the default when output is not to a terminal.",
                Values = ["v"]
            },
            new ArgumentDescription {
                Name = "W",
                Description = "Display whiteouts when scanning directories. (-S) flag).",
                Values = ["W"]
            },
            new ArgumentDescription {
                Name = "w",
                Description = "Force raw printing of non-printable characters.  This is the default when output is not to a terminal.",
                Values = ["w"]
            },
            new ArgumentDescription {
                Name = "x",
                Description = "The same as -C, except that the multi-column output is produced with entries sorted across, rather than down, the columns.",
                Values = ["x"]
            },
        ],
        ArgumentGroups = [
            new ArgumentGroup()
            {
                Name = "Flag",
                Arguments = ["x", "W", "w", "v", "u", "T", "t", "S", "s", "R", "r", "q", "P", "p", "O", "o", "n", "m", "L", "l", "k", "i", "H", "h", "G", "g", "F", "f", "d", "C", "c", "B", "b", "A", "a", "1", "@"]
            }
        ],
    };
}
*/