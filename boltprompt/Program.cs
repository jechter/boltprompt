using boltprompt;
using System.CommandLine;
using NiceIO;

var scopeOption = new Option<ShellInstaller.InstallScope>(
    name: "--scope",
    description: "scope for installation",
    getDefaultValue: () => ShellInstaller.InstallScope.user);

var outputCommandOption = new Option<string?>(
    name: "--output-command",
    description: "Output command");

var rootCommand = new RootCommand("boltprompt interactive shell command prompt editor")
{
    outputCommandOption,
    FigConvertCommand(), 
    InstallCommand(), 
    UninstallCommand(),
    SetupTerminalCommand(),
    ConfigCommand()
};

rootCommand.SetHandler((outputCommand) => { new MainLoop(outputCommand).Run(); }, outputCommandOption);

await rootCommand.InvokeAsync(args);
return;


Command InstallCommand()
{
    var installCommand = new Command("install", "Install boltprompt for current shell") { scopeOption };
    installCommand.SetHandler(ShellInstaller.InstallIntoCurrentShell, scopeOption);
    return installCommand;
}

Command UninstallCommand()
{
    var uninstallCommand = new Command("uninstall", "Uninstall boltprompt for current shell") { scopeOption };
    uninstallCommand.SetHandler(ShellInstaller.UninstallFromCurrentShell, scopeOption);
    return uninstallCommand;
}

Command SetupTerminalCommand()
{
    var setupTerminalCommand = new Command("setup-terminal", "Set up Terminal.app for boltprompt");
    setupTerminalCommand.SetHandler(TerminalUtility.SetupTerminal);
    return setupTerminalCommand;
}

Command FigConvertCommand()
{
    var outputDirOption = new Option<string?>(
        name: "--output-dir",
        description: "Output directory");

    var figConvertCommand = new Command("fig-convert-all", "Convert all fig commands") { outputDirOption };
    figConvertCommand.SetHandler(async outputDir =>
    {
        if (outputDir == null)
            outputDir = NPath.CurrentDirectory.Combine("fig-converted").ToString();
        await new FigCommandInfoSupplier().ConvertAll(outputDir);
    }, outputDirOption);
    return figConvertCommand;
}

Command ConfigCommand()
{
    var propertyOption = new Option<string?>(name: "--property");
    var prefixOption = new Option<string?>(name: "--prefix");
    var configListCommand = new Command("list", "list all configuration properties") { propertyOption, prefixOption };
    configListCommand.SetHandler((property, prefix) =>
    {
        if (property != null)
            Configuration.ListPropertyValues(property, prefix);
        else
            Configuration.ListProperties();
    }, propertyOption, prefixOption);

    var valueArgument = new Argument<string>("value");
    var propertyArgument = new Argument<string>("property");
    var configGetCommand = new Command("get", "print a configuration property") { propertyArgument };
    configGetCommand.SetHandler(property =>
    {
        var result = Configuration.Instance.Get(property);
        Console.WriteLine($"{property} = {result}");
    }, propertyArgument);

    var configSetCommand = new Command("set", "set a configuration property") { propertyArgument, valueArgument };
    configSetCommand.SetHandler(Configuration.Instance.Set, propertyArgument, valueArgument);

    return new ("config", "Configure boltprompt settings")
    {
        configGetCommand,
        configSetCommand,
        configListCommand
    };
}
