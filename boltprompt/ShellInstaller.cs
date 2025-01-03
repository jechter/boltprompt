using NiceIO;

namespace boltprompt;

public static class ShellInstaller
{
    private const string BoltpromptConfigComment =
        "# This line is needed for boltprompt, remove if boltprompt is uninstalled.";
    
    public enum InstallScope
    {
        session,
        user,
        system
    };
    
    static IEnumerable<NPath> ConfigPathsForCurrentShell(InstallScope installScope)
    {
        var shellPath = Environment.GetEnvironmentVariable("SHELL")?.ToNPath();
        switch (shellPath?.FileName)
        {
            case "bash":
                if (installScope == InstallScope.system)
                    yield return NPath.HomeDirectory.Combine("/etc/bashrc");
                else
                {
                    yield return NPath.HomeDirectory.Combine(".bashrc");
                    yield return NPath.HomeDirectory.Combine(".bash_profile");
                }
                break;
            case "zsh":
                if (installScope == InstallScope.system)
                    yield return NPath.HomeDirectory.Combine("/etc/zshrc");
                else
                    yield return NPath.HomeDirectory.Combine(".zshrc");
                break;
            default:
                throw new NotSupportedException($"Unknown shell {shellPath}");
        }        
    }


    public static void InstallIntoCurrentShell(InstallScope installScope)
    {
        DoUninstallFromCurrentShell(installScope);

        var installLine = $"source {Paths.boltpromptSupportFilesDir.Combine("setup_boltprompt.sh")} {BoltpromptConfigComment}";
        
        if (installScope > InstallScope.session)
        {
            foreach (var p in ConfigPathsForCurrentShell(installScope))
                p.WriteAllLines(p.FileExists() ? p.ReadAllLines().Append(installLine).ToArray() : [installLine]);
            BufferedConsole.ForegroundColor = BufferedConsole.ConsoleColor.LightGreen;
            BufferedConsole.WriteLine();
            BufferedConsole.Write("boltprompt has been installed for this shell.\n");
            BufferedConsole.Bold = true;
            BufferedConsole.Write("Open a new terminal session to use boltprompt!\n");
            BufferedConsole.Bold = false;
            BufferedConsole.WriteLine();
            BufferedConsole.ResetColor();
            if (TerminalUtility.TerminalHasBoltpromptConfigurationAction)
            {
                BufferedConsole.Write("Use ");
                BufferedConsole.Bold = true;
                BufferedConsole.Write("`boltprompt setup-terminal`");
                BufferedConsole.Bold = false;
                BufferedConsole.Write(" to configure your terminal to use the right font and ignore boltprompt processes when closing the terminal session.\n");
            }

            BufferedConsole.Flush();
        }

        File.WriteAllText("/tmp/custom-command", installLine);
        Environment.Exit(0);
    }
    

    static void DoUninstallFromCurrentShell(InstallScope installScope)
    {
        if (installScope > InstallScope.session)
        {
            foreach (var p in ConfigPathsForCurrentShell(installScope))
                if (p.FileExists())
                    p.WriteAllLines(p.ReadAllLines().Where(l => !l.TrimEnd().EndsWith(BoltpromptConfigComment))
                        .ToArray());
        }
    }

    public static void UninstallFromCurrentShell(InstallScope installScope)
    {
        DoUninstallFromCurrentShell(installScope);
        Console.WriteLine("boltprompt has been uninstalled for this shell. Open a new terminal session to get back to your normal prompt.");
    }
}