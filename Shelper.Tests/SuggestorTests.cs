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
        History.LoadTestHistory([]);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var p in _pathsToCleanup)
            p.Delete();
        _pathsToCleanup.Clear();
    }
    
    [Test]
    public void CanFindExecutablesInSearchPrefix()
    {
        var testDir = new NPath("localTestDir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        MakeTestExecutable(testDir.Combine("testExecutable"));
        testDir.Combine("testFile").WriteAllText("Just a file");

        var suggestions = new Suggestor().SuggestionsForPrompt("localTestDir/");
        
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("localTestDir/testExecutable"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("localTestDir/testFile"));
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
        return new Suggestor().SuggestionsForPrompt($"./testExecutable{extraPrompt}");
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
    public void SuggestionsRespectArgumentGroupOrderForFlags()
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
    public void SuggestionsRespectArgumentGroupOrderForKeywords()
    {
        var ci = new CommandInfo
        {
            Arguments =
            [
                [
                    new("a"),
                    new("b"),
                    new("c"),
                ],
                [
                    new("d"),
                    new("e"),
                    new("f"),
                ]
            ]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " ");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("a"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("b"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("c"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("d"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("e"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("f"));
        
        suggestions = GetSuggestionsForTestExecutable(ci, " a ");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("a"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("b"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("c"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("d"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("e"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("f"));
         
        suggestions = GetSuggestionsForTestExecutable(ci, " d ");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("a"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("b"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("c"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("d"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("e"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("f"));
    }
    
    [Test]
    public void CanGetSuggestionsForSubArgument()
    {
        var ci = new CommandInfo
        {
            Arguments =
            [[
                new("foo") {
                    Arguments = [[
                        new ("flip"), 
                        new ("flop") 
                    ]]
                },
                new("faz")
            ]]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " f");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("foo"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("faz"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("flip"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("flop"));
        suggestions = GetSuggestionsForTestExecutable(ci, " foo ");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("foo"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("faz"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("flip"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("flop"));
    }
    

    [Test]
    public void CanGetSuggestionsForCommands()
    {
        var testExePath1 = NPath.CreateTempDirectory("testdir-");
        MakeTestExecutable(testExePath1.Combine("testExecutable"));
        _pathsToCleanup.Add(testExePath1);
        Environment.SetEnvironmentVariable("PATH", testExePath1.ToString());

        var ci = new CommandInfo
        {
            Arguments = [[ new("") { Type = CommandInfo.ArgumentType.CommandName }]]
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
    
    [Test]
    public void SuggestionsForFileSystemEntriesAreEscaped()
    {
        var testDir = new NPath("testDir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        testDir.Combine("this is a directory").CreateDirectory();

        var ci = new CommandInfo
        {
            Arguments = [[ new("") { Type = CommandInfo.ArgumentType.FileSystemEntry }]]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " testDir/");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("testDir/this\\ is\\ a\\ directory/"));
    }
    
    [Test]
    public void CanSuggestFileSystemEntriesInsideEscapedPath()
    {
        var testDir = new NPath("test dir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        testDir.Combine("test sub dir").CreateDirectory();
        testDir.Combine("test sub dir").Combine("test file").WriteAllText("bla");

        var ci = new CommandInfo
        {
            Arguments = [[ new("") { Type = CommandInfo.ArgumentType.FileSystemEntry }]]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " test\\ dir/");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("test\\ dir/test\\ sub\\ dir/"));
    }


    [Test]
    public void CanGetSuggestionsFromHistory()
    {
        History.LoadTestHistory(["foo", "bar", "foo bar"]);
        var suggestions = new Suggestor().SuggestionsForPrompt("f");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("foo"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("foo bar"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("bar"));
    }

    [Test]
    public void FileSystemArgumentSuggestionsFromHistoryComeFirst()
    {
        var testDir = new NPath("testDir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        testDir.Combine("directory").CreateDirectory();
        testDir.Combine("file").WriteAllText("bla");
        
        var ci = new CommandInfo
        {
            Arguments = [[ new("") { Type = CommandInfo.ArgumentType.FileSystemEntry }]]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " testDir/");
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new [] {"testDir/directory/", "testDir/file"}));
        
        History.LoadTestHistory(["./testExecutable testDir/file"]);
        suggestions = GetSuggestionsForTestExecutable(ci, " testDir/");
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new [] {"testDir/file", "testDir/directory/"}));
    }
    
    [Test]
    public void PartialFileSystemArgumentSuggestionsFromHistoryComeFirst()
    {
        var testDir = new NPath("testDir").MakeAbsolute().CreateDirectory();   
        var testSubDir = testDir.Combine("testSubDir").CreateDirectory();   
        testDir.Combine("otherTestSubDir").CreateDirectory();   
        testSubDir.Combine("file").WriteAllText("bla");
        _pathsToCleanup.Add(testDir);
        
        var ci = new CommandInfo
        {
            Arguments = [[ new("") { Type = CommandInfo.ArgumentType.FileSystemEntry }]]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " testDir/");
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new [] {"testDir/otherTestSubDir/", "testDir/testSubDir/"}));
        
        History.LoadTestHistory(["./testExecutable testDir/testSubDir/file"]);
        suggestions = GetSuggestionsForTestExecutable(ci, " testDir/");
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new [] {"testDir/testSubDir/", "testDir/otherTestSubDir/"}));
    }
    
    [Test]
    public void FlagArgumentSuggestionsFromHistoryComeFirst()
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
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new [] {"-a", "-b", "-c"}));
        
        History.LoadTestHistory(["./testExecutable -c"]);
        suggestions = GetSuggestionsForTestExecutable(ci, " -");
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new [] {"-c", "-a", "-b"}));

        History.LoadTestHistory(["./testExecutable -b", "./testExecutable -c"]);
        suggestions = GetSuggestionsForTestExecutable(ci, " -");
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new [] {"-c", "-b", "-a"}));
    }
    
    [Test]
    public void CanSuggestEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("FOO", "Test Value");
        var suggestions = new Suggestor().SuggestionsForPrompt("$");
        Assert.That(suggestions, Does.Contain(new Suggestion("$FOO") {Description = "Test Value"}));
    }
}