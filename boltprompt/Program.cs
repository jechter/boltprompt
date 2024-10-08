using boltprompt;

using System.CommandLine;

var figConvertOption = new Option<string>(
    name: "--convertAllFigCommands",
    description: "Convert all fig commands");

var rootCommand = new RootCommand("boltprompt interactive shell command prompt editor");
rootCommand.AddOption(figConvertOption);

rootCommand.SetHandler(async (figConvert) => 
    {
        if (figConvert != null)
        {
            Console.WriteLine($"figConvert: {figConvert}");
            await new FigCommandInfoSupplier().ConvertAll(figConvert);
            Environment.Exit(0);
        }
    },
    figConvertOption);

await rootCommand.InvokeAsync(args);

new MainLoop().Run();