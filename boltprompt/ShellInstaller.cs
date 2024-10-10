using CliWrap;
using CliWrap.Buffered;
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
            Console.WriteLine("boltprompt has been installed for this shell. Open a new terminal session to use it.");
            if (Environment.GetEnvironmentVariable("TERM_PROGRAM") == "Apple_Terminal")
                Console.WriteLine(
                    "Use `boltprompt setup-terminal` to configure Terminal.app to use the right font and ignore boltprompt processes when closing the terminal session.");
        }

        File.WriteAllText("/tmp/custom-command", installLine);
        Environment.Exit(0);
    }
    
    public static void SetupTerminal()
    {
        var terminal = Environment.GetEnvironmentVariable("TERM_PROGRAM");
        if (terminal != "Apple_Terminal")
            throw new NotSupportedException($"boltprompt only knows how to configure fonts for Apple's Terminal.app. You are using {terminal}, which we don't know how to set up.");
        Cli.Wrap("bash")
            .WithArguments(Paths.boltpromptSupportFilesDir.Combine("setup-terminal.sh").ToString())
            .WithWorkingDirectory(Paths.boltpromptSupportFilesDir.ToString())
            .ExecuteBufferedAsync()
            .GetAwaiter()
            .GetResult();
        Console.WriteLine("Terminal.app has been set up for boltprompt.");
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
        Console.WriteLine("boltprompt has been uninstalled for this shell.");
    }
}