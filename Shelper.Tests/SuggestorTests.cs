using Mono.Unix.Native;
using NiceIO;

namespace Shelper.Tests;

public class SuggestorTests
{
    private NPath MakeTestExecutable(NPath path)
    {
        path.WriteAllText("#!/bin/sh\necho test");
        Syscall.chmod(path.ToString(), FilePermissions.S_IRWXU);
        return path;
    }

    private readonly HashSet<NPath> _pathsToCleanup = [];
    
    [SetUp]
    public void Setup()
    {
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var p in _pathsToCleanup)
            p.Delete();
    }

    [Test]
    public void CanFindExecutablesInWorkingDir()
    {
        _pathsToCleanup.Add(MakeTestExecutable(new NPath("localTestExecutable").MakeAbsolute()));
        _pathsToCleanup.Add(new NPath("localTestFile").MakeAbsolute().WriteAllText("Just a file"));

        var suggestions = new Suggestor().SuggestionsForPrompt("l");
        
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("localTestExecutable"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("localTestFile"));
    }
    
    [Test]
    public void CanFindExecutablesInSearchPrefix()
    {
        var testDir = new NPath("localTestDir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        MakeTestExecutable(testDir.Combine("testExecutable"));
        testDir.Combine("testFile").WriteAllText("Just a file");

        var suggestions = new Suggestor().SuggestionsForPrompt("testDir/");
        
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("testDir/testExecutable"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("testDir/testFile"));
    }
    
    [Test]
    public void CanFindExecutablesInPathEnvironment()
    {
        var testExePath1 = NPath.CreateTempDirectory("testdir-");
        MakeTestExecutable(testExePath1.Combine("pathTestExecutable"));
        testExePath1.Combine("pathTestFile").WriteAllText("Just a file");
        _pathsToCleanup.Add(testExePath1);
        
        var testExePath2 = NPath.CreateTempDirectory("testdir-");
        MakeTestExecutable(testExePath2.Combine("anotherPathTestExecutable"));
        testExePath2.Combine("anotherPathTestFile").WriteAllText("Just a file");
        _pathsToCleanup.Add(testExePath2);

        Environment.SetEnvironmentVariable("PATH", $"{testExePath1}:{testExePath2}");
        
        var suggestions = new Suggestor().SuggestionsForPrompt("p");

        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("pathTestExecutable"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("pathTestFile"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("anotherPathTestFile"));
        
        suggestions = new Suggestor().SuggestionsForPrompt("a");

        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("anotherPathTestExecutable"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("anotherPathTestFile"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("pathTestExecutable"));
    }

    Suggestion[] GetSuggestionsForTestExecutable(CommandInfo ci, string extraPrompt = "")
    {
        _pathsToCleanup.Add(MakeTestExecutable(new NPath("testExecutable").MakeAbsolute()));
        KnownCommands.AddCommandInfo("testExecutable", ci);
        return new Suggestor().SuggestionsForPrompt($"testExecutable{extraPrompt}");
    }
    
    [Test]
    public void SuggestionContainsCommandDescription()
    {
        const string testDescription = "This is a test command";
        var suggestions = GetSuggestionsForTestExecutable(new ()
        {
            Description = testDescription
        });
        Assert.That(suggestions, Has.Length.EqualTo(1));
        Assert.That(suggestions[0].Description, Is.EqualTo(testDescription));
    }
    
    [Test]
    public void CanGetSuggestionsForFlags()
    {
        var ci = new CommandInfo
        {
            Arguments =
            [
                [
                    new("a") { Type = CommandInfo.ArgumentType.Flag },
                    new("b") { Type = CommandInfo.ArgumentType.Flag },
                    new("c") { Type = CommandInfo.ArgumentType.Flag },
                ]
            ]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " -");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-a"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-b"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-c"));
    }
    
    [Test]
    public void CanGetSuggestionsForMultipleFlags()
    {
        var ci = new CommandInfo
        {
            Arguments =
            [
                [
                    new("a") { Type = CommandInfo.ArgumentType.Flag },
                    new("b") { Type = CommandInfo.ArgumentType.Flag },
                    new("c") { Type = CommandInfo.ArgumentType.Flag },
                ]
            ]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " -");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-a"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-b"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-c"));
        
        suggestions = GetSuggestionsForTestExecutable(ci, " -a");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-ab"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-ac"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-aa"));

        suggestions = GetSuggestionsForTestExecutable(ci, " -ab");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-abc"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-aba"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-abb"));
    }
    
    [Test]
    public void CanGetSuggestionsForRepeatableFlags()
    {
        var ci = new CommandInfo
        {
            Arguments =
            [
                [
                    new("a") { Type = CommandInfo.ArgumentType.Flag, Repeat = true },
                    new("b") { Type = CommandInfo.ArgumentType.Flag },
                    new("c") { Type = CommandInfo.ArgumentType.Flag },
                ]
            ]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " -");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-a"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-b"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-c"));
        
        suggestions = GetSuggestionsForTestExecutable(ci, " -a");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-aa"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-ab"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-ac"));

        suggestions = GetSuggestionsForTestExecutable(ci, " -ab");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-aba"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-abc"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-abb"));
    }
    
    [Test]
    public void SuggestionForFlagHasDescription()
    {
        var ci = new CommandInfo
        {
            Arguments =
            [
                [
                    new("a") { Type = CommandInfo.ArgumentType.Flag, Description = "This is flag a" },
                    new("b") { Type = CommandInfo.ArgumentType.Flag, Description = "This is flag b" },
                ]
            ]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " -a");
        Assert.That(suggestions[0].Description, Is.EqualTo("This is flag a"));

        suggestions = GetSuggestionsForTestExecutable(ci, " -ab");
        Assert.That(suggestions[0].Description, Is.EqualTo("This is flag b"));
    }
    
    [Test]
    public void SuggestionsRespectArgumentGroupOrder()
    {
        var ci = new CommandInfo
        {
            Arguments =
            [
                [
                    new("a") { Type = CommandInfo.ArgumentType.Flag },
                    new("b") { Type = CommandInfo.ArgumentType.Flag },
                    new("c") { Type = CommandInfo.ArgumentType.Flag },
                ],
                [
                    new("d") { Type = CommandInfo.ArgumentType.Flag },
                    new("e") { Type = CommandInfo.ArgumentType.Flag },
                    new("f") { Type = CommandInfo.ArgumentType.Flag },
                ]
            ]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " -");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-a"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-b"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-c"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-d"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-e"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-f"));
        
        suggestions = GetSuggestionsForTestExecutable(ci, " -a");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-aa"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-ab"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-ac"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-ad"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-ae"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-af"));
        
        suggestions = GetSuggestionsForTestExecutable(ci, " -d");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-da"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-db"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-dc"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-dd"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-de"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-df"));
    }

    [Test]
    public void CanGetSuggestionsForCommands()
    {
        var ci = new CommandInfo
        {
            Arguments = [[ new("") { Type = CommandInfo.ArgumentType.Command }]]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " ");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("testExecutable"));
    }

    [Test]
    [TestCase(CommandInfo.ArgumentType.FileSystemEntry)]
    [TestCase(CommandInfo.ArgumentType.File)]
    [TestCase(CommandInfo.ArgumentType.Directory)]
    public void CanGetSuggestionsForFileSystemEntries(CommandInfo.ArgumentType type)
    {
        var testDir = new NPath("testDir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        testDir.Combine("directory").CreateDirectory();
        testDir.Combine("file").WriteAllText("bla");

        var ci = new CommandInfo
        {
            Arguments = [[ new("") { Type = type }]]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " testDir/");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("testDir/directory/"));
        Assert.That(suggestions.Select(s => s.Text.Trim()).Contains("testDir/file"), Is.EqualTo(type != CommandInfo.ArgumentType.Directory));
    }
}