using boltprompt;

using System.CommandLine;

var figConvertOption = new Option<string?>(
    name: "--convertAllFigCommands",
    description: "Convert all fig commands");

var installOption = new Option<bool>(
    name: "--install",
    description: "Install boltprompt for current shell");

var uninstallOption = new Option<bool>(
    name: "--uninstall",
    description: "Uninstall boltprompt for current shell");

var setupTerminalOption = new Option<bool>(
    name: "--setup-terminal",
    description: "Set up Terminal.app for boltprompt");

var rootCommand = new RootCommand("boltprompt interactive shell command prompt editor");
rootCommand.AddOption(figConvertOption);
rootCommand.AddOption(installOption);
rootCommand.AddOption(uninstallOption);
rootCommand.AddOption(setupTerminalOption);

rootCommand.SetHandler(async (figConvert, install, uninstall, setupTerminal) => 
    {
        if (figConvert != null)
        {
            await new FigCommandInfoSupplier().ConvertAll(figConvert);
            Environment.Exit(0);
        }

        if (install)
        {
            ShellInstaller.InstallIntoCurrentShell();
            Environment.Exit(0);
        }

        if (uninstall)
        {
            ShellInstaller.UninstallFromCurrentShell();
            Environment.Exit(0);
        }

        if (setupTerminal)
        {
            ShellInstaller.SetupTerminal();
            Environment.Exit(0);
        }
    },
    figConvertOption, installOption, uninstallOption, setupTerminalOption);

await rootCommand.InvokeAsync(args);

new MainLoop().Run();