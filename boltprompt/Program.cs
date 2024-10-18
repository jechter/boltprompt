using boltprompt;

using System.CommandLine;
using NiceIO;

var outputDirOption = new Option<string?>(
    name: "--output-dir",
    description: "Output directory");

var scopeOption = new Option<ShellInstaller.InstallScope>(
    name: "--scope",
    description: "scope for installation",
    getDefaultValue: () => ShellInstaller.InstallScope.user);

var figConvertCommand = new Command("fig-convert-all", "Convert all fig commands") { outputDirOption };
figConvertCommand.SetHandler(async outputDir =>
{
    if (outputDir == null)
        outputDir = NPath.CurrentDirectory.Combine("fig-converted").ToString();
    await new FigCommandInfoSupplier().ConvertAll(outputDir);
    Environment.Exit(0);
}, outputDirOption);

var installCommand = new Command("install", "Install boltprompt for current shell") { scopeOption };
installCommand.SetHandler(scope =>
{
    ShellInstaller.InstallIntoCurrentShell(scope);
    Environment.Exit(0);
}, scopeOption);

var uninstallCommand = new Command("uninstall", "Uninstall boltprompt for current shell") { scopeOption };
uninstallCommand.SetHandler(scope =>
{
    ShellInstaller.UninstallFromCurrentShell(scope);
    Environment.Exit(0);
}, scopeOption);

var setupTerminalCommand = new Command("setup-terminal", "Set up Terminal.app for boltprompt") { scopeOption };
setupTerminalCommand.SetHandler(scope =>
{
    ShellInstaller.SetupTerminal();
    Environment.Exit(0);
}, scopeOption);

var propertyOption = new Option<string?>(name: "--property");

var configListCommand = new Command("list", "list all configuration properties") { propertyOption };
configListCommand.SetHandler(property =>
{
    if (property != null)
        Configuration.ListPropertyValues(property);
    else
        Configuration.ListProperties();
    Environment.Exit(0);
}, propertyOption);

var valueArgument = new Argument<string>("value");
var propertyArgument = new Argument<string>("property");
var configGetCommand = new Command("get", "print a configuration property") { propertyArgument };
configGetCommand.SetHandler(property =>
{
    var result = Configuration.Instance.Get(property);
    Console.WriteLine($"{property} = {result}");
    Environment.Exit(0);
}, propertyArgument);

var configSetCommand = new Command("set", "set a configuration property") { propertyArgument, valueArgument };
configSetCommand.SetHandler((property, value) =>
{
    Configuration.Instance.Set(property, value);
    Environment.Exit(0);
}, propertyArgument, valueArgument);

var configCommand = new Command("config", "Configure boltprompt settings")
{
    configGetCommand,
    configSetCommand,
    configListCommand
};

var rootCommand = new RootCommand("boltprompt interactive shell command prompt editor")
{
    figConvertCommand, 
    installCommand, 
    uninstallCommand,
    setupTerminalCommand,
    configCommand
};


rootCommand.SetHandler(_ => { new MainLoop().Run(); });

await rootCommand.InvokeAsync(args);

