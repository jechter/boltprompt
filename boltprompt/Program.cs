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

var rootCommand = new RootCommand("boltprompt interactive shell command prompt editor");
var figConvertCommand = new Command("fig-convert-all", "Convert all fig commands") { outputDirOption };
var installCommand = new Command("install", "Install boltprompt for current shell") { scopeOption };
var uninstallCommand = new Command("uninstall", "Uninstall boltprompt for current shell") { scopeOption };
var setupTerminalCommand = new Command("setup-terminal", "Set up Terminal.app for boltprompt") { scopeOption };

rootCommand.AddCommand(figConvertCommand);
rootCommand.AddCommand(installCommand);
rootCommand.AddCommand(uninstallCommand);
rootCommand.AddCommand(setupTerminalCommand);

figConvertCommand.SetHandler(async outputDir =>
{
    if (outputDir == null)
        outputDir = NPath.CurrentDirectory.Combine("fig-converted").ToString();
    await new FigCommandInfoSupplier().ConvertAll(outputDir);
    Environment.Exit(0);
}, outputDirOption);

installCommand.SetHandler(scope =>
{
    ShellInstaller.InstallIntoCurrentShell(scope);
    Environment.Exit(0);
}, scopeOption);

uninstallCommand.SetHandler(scope =>
{
    ShellInstaller.UninstallFromCurrentShell(scope);
    Environment.Exit(0);
}, scopeOption);

setupTerminalCommand.SetHandler(scope =>
{
    ShellInstaller.SetupTerminal();
    Environment.Exit(0);
}, scopeOption);

rootCommand.SetHandler(_ => { new MainLoop().Run(); });

await rootCommand.InvokeAsync(args);

