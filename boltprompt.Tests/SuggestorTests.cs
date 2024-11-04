using System.Runtime.InteropServices;
using CliWrap;
using CliWrap.Buffered;
using Mono.Unix.Native;
using NiceIO;

namespace boltprompt.Tests;

public class SuggestorTests
{
    private NPath MakeTestExecutable(NPath path)
    {
        path.WriteAllText("#!/bin/sh\necho test");
        Syscall.chmod(path.ToString(), FilePermissions.S_IRWXU);
        return path;
    }

    private readonly HashSet<NPath> _pathsToCleanup = [];
    private string? _oldPath;
    
    private static ManualResetEvent? _eventToWaitFor;

    private static Action<T> SetupWaitEvent<T>(Func<T, bool> validator)
    {
        _eventToWaitFor = new ManualResetEvent(false);
        return t =>
        {
            if (validator(t))
                _eventToWaitFor.Set();
        };
    }

    private static Action SetupWaitEvent()
    {
        _eventToWaitFor = new ManualResetEvent(false);
        return () =>
        {
            _eventToWaitFor.Set();
        };
    }
    
    [SetUp]
    public void Setup()
    {
        _oldPath = Environment.GetEnvironmentVariable("PATH");
        History.LoadTestHistory([]);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var p in _pathsToCleanup)
            p.Delete();
        _pathsToCleanup.Clear();
        Environment.SetEnvironmentVariable("PATH", _oldPath);
    }

    [Test]
    public void CommandInfoOptionalIsSerializedCorrectly()
    {
        var ci = new CommandInfo()
        {
            Arguments =
            [
                new([])
                {
                    Optional = false
                },
                new([])
                {
                    Optional = true
                },

            ]
        };
        
        var json = ci.Serialize();
        var deserialized = CommandInfo.Deserialize(json);
        Assert.That(deserialized.Arguments[0].Optional, Is.EqualTo(ci.Arguments[0].Optional));
        Assert.That(deserialized.Arguments[1].Optional, Is.EqualTo(ci.Arguments[1].Optional));
    }
    
    [Test]
    public void CanFindExecutablesInSearchPrefix()
    {
        var testDir = new NPath("localTestDir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        MakeTestExecutable(testDir.Combine("testExecutable"));
        testDir.Combine("testFile").WriteAllText("Just a file");

        var suggestions = Suggestor.SuggestionsForPrompt("localTestDir/");
        
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
        Suggestor.Init();
        
        var suggestions = Suggestor.SuggestionsForPrompt("p");

        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("pathTestExecutable"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("pathTestFile"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("anotherPathTestFile"));
        
        suggestions = Suggestor.SuggestionsForPrompt("a");

        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("anotherPathTestExecutable"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("anotherPathTestFile"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("pathTestExecutable"));
    }

    Suggestion[] GetSuggestionsForTestExecutable(CommandInfo ci, string extraPrompt = "")
    {
        _pathsToCleanup.Add(MakeTestExecutable(new NPath("testExecutable").MakeAbsolute()));
        KnownCommands.AddCommandInfo("testExecutable", ci);
        return Suggestor.SuggestionsForPrompt($"./testExecutable{extraPrompt}");
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
                new([
                    new("a") { Type = CommandInfo.ArgumentType.Flag },
                    new("b") { Type = CommandInfo.ArgumentType.Flag },
                    new("c") { Type = CommandInfo.ArgumentType.Flag },
                ])
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
                new([
                    new("a") { Type = CommandInfo.ArgumentType.Flag },
                    new("b") { Type = CommandInfo.ArgumentType.Flag },
                    new("c") { Type = CommandInfo.ArgumentType.Flag },
                ])
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
                new([
                    new("a") { Type = CommandInfo.ArgumentType.Flag, Repeat = true },
                    new("b") { Type = CommandInfo.ArgumentType.Flag },
                    new("c") { Type = CommandInfo.ArgumentType.Flag },
                ])
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
                new([
                    new("a") { Type = CommandInfo.ArgumentType.Flag, Description = "This is flag a" },
                    new("b") { Type = CommandInfo.ArgumentType.Flag, Description = "This is flag b" },
                ])
            ]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " -a");
        Assert.That(suggestions[0].Description, Is.EqualTo("This is flag a"));

        suggestions = GetSuggestionsForTestExecutable(ci, " -ab");
        Assert.That(suggestions[0].Description, Is.EqualTo("This is flag b"));
    }
    
    [Test]
    [Ignore("not supported, either fix or redefine behaviour")]
    public void SuggestionsRespectArgumentGroupOrderForFlags()
    {
        var ci = new CommandInfo
        {
            Arguments =
            [
                new([
                    new("a") { Type = CommandInfo.ArgumentType.Flag },
                    new("b") { Type = CommandInfo.ArgumentType.Flag },
                    new("c") { Type = CommandInfo.ArgumentType.Flag },
                ]) { Optional = true },
                new([
                    new("d") { Type = CommandInfo.ArgumentType.Flag },
                    new("e") { Type = CommandInfo.ArgumentType.Flag },
                    new("f") { Type = CommandInfo.ArgumentType.Flag },
                ])
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
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-ad"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-ae"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-af"));
         
        suggestions = GetSuggestionsForTestExecutable(ci, " -d");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-da"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-db"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-dc"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("-dd"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-de"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("-df"));
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void SuggestionsRespectArgumentGroupOrderForKeywords(bool firstGroupOptional)
    {
        var ci = new CommandInfo
        {
            Arguments =
            [
                new([
                    new("a"),
                    new("b"),
                    new("c"),
                ]) { Optional = firstGroupOptional },
                new([
                    new("d"),
                    new("e"),
                    new("f"),
                ])
            ]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " ");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("a"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("b"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("c"));
        if (firstGroupOptional)
        {
            Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("d"));
            Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("e"));
            Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("f"));
        }
        else
        {
            Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("d"));
            Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("e"));
            Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("f"));
        }

        suggestions = GetSuggestionsForTestExecutable(ci, " a ");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("a"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("b"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("c"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("d"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("e"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("f"));

        if (!firstGroupOptional) return;

        suggestions = GetSuggestionsForTestExecutable(ci, " d ");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("a"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("b"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("c"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("d"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("e"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("f"));
    }
    
    [Test]
    public void ArgumentParserWillMatchPath()
    {
        var ci = new CommandInfo
        {
            Arguments =
            [
                new([
                    new("a") { Type = CommandInfo.ArgumentType.FileSystemEntry }
                ]) { Optional = false },
                new([
                    new("b"),
                    new("c"),
                    new("d"),
                ])
            ]
        };
        
        var suggestions = GetSuggestionsForTestExecutable(ci, " ");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("b"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("c"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("d"));

        suggestions = GetSuggestionsForTestExecutable(ci, " hello ");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("b"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("c"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("d"));
    }
    
    [Test]
    public void ArgumentParserWillMatchOnlyOnePath()
    {
        var testDir = new NPath("localTestDir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        testDir.Combine("testFile1").WriteAllText("Just a file");
        testDir.Combine("testFile2").WriteAllText("Just a file");

        var ci = new CommandInfo
        {
            Arguments =
            [
                new([
                    new("a") { Type = CommandInfo.ArgumentType.FileSystemEntry, Description = "a" }
                ]) { Optional = false },
                new([
                    new("b") { Type = CommandInfo.ArgumentType.FileSystemEntry, Description = "b" }
                ])
            ]
        };
        
        var suggestions = GetSuggestionsForTestExecutable(ci, " localTestDir/testFile1");
        Assert.That(suggestions.Select(s => s.Description?.Trim()), Does.Contain("a"));
        Assert.That(suggestions.Select(s => s.Description?.Trim()), Does.Not.Contain("b"));
        
        suggestions = GetSuggestionsForTestExecutable(ci, " localTestDir/testFile1 localTestDir/testFile2");
        Assert.That(suggestions.Select(s => s.Description?.Trim()), Does.Not.Contain("a"));
        Assert.That(suggestions.Select(s => s.Description?.Trim()), Does.Contain("b"));
    }
    
    [Test]
    public void ArgumentParserCanHandleQuotes()
    {
        var ci = new CommandInfo
        {
            Arguments =
            [
                new([
                    new("a") { Type = CommandInfo.ArgumentType.String, Description = "a" }
                ]) { Optional = false },
                new([
                    new("b") { Type = CommandInfo.ArgumentType.String, Description = "b" }
                ])
            ]
        };
        
        var suggestions = GetSuggestionsForTestExecutable(ci, " \"bla bla");
        Assert.That(suggestions.Select(s => s.Description?.Trim()), Does.Contain("a"));
        Assert.That(suggestions.Select(s => s.Description?.Trim()), Does.Not.Contain("b"));
        
        suggestions = GetSuggestionsForTestExecutable(ci, " \"bla bla\" \"blub blub");
        Assert.That(suggestions.Select(s => s.Description?.Trim()), Does.Not.Contain("a"));
        Assert.That(suggestions.Select(s => s.Description?.Trim()), Does.Contain("b"));
    }
    
    [Test]
    public void SuggestionsRespectDontAllowMultiple()
    {
        var ci = new CommandInfo
        {
            Arguments =
            [
                new([
                    new("a"),
                    new("b"),
                    new("c"),
                ]) { Optional = true, DontAllowMultiple = true}, 
                new([ 
                    new("d"),
                    new("e"),
                    new("f"),
                ]) { DontAllowMultiple = true }
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
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("b"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("c"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("d"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("e"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("f"));
         
        suggestions = GetSuggestionsForTestExecutable(ci, " d ");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("a"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("b"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("c"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("d"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("e"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("f"));
    }
    
    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public void CanGetSuggestionsForSubArgument(bool subArgumentIsOptional)
    {
        var ci = new CommandInfo
        {
            Arguments =
            [new([
                new("foo") {
                    Arguments = [new([
                        new ("flip"), 
                        new ("flop") 
                    ]) { Optional = subArgumentIsOptional }]
                },
                new("faz")
            ])]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " f");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("foo"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("faz"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("flip"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("flop"));
        suggestions = GetSuggestionsForTestExecutable(ci, " foo ");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("foo"));
        if (subArgumentIsOptional)
            Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("faz"));
        else
            Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("faz"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("flip"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("flop"));
    }
    
    [Test]
    public void CanGetSuggestionsForSubIfMainArgumentHasSameName()
    {
        var ci = new CommandInfo
        {
            Arguments =
            [new([
                new("foo") {
                    Arguments = [new([
                        new("faz") {
                            Arguments = [new([
                                new ("flip"), 
                                new ("flop") 
                            ])]
                        }
                    ])]
                },
                new("faz")
            ])]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " foo faz ");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("foo"));
        //Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("faz"));
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
        Suggestor.Init();

        var ci = new CommandInfo
        {
            Arguments = [new([ new("") { Type = CommandInfo.ArgumentType.CommandName }])]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " ");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("testExecutable"));
    }
    
    [Test]
    public void CanGetSuggestionsForCustomArgument()
    {
        var testDir = new NPath("testDir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        testDir.Combine("customArgList").WriteAllText("foo\nbar\nbaz\n");

        var ci = new CommandInfo
        {
            Arguments = [
                new([ new("arg1") { Type = CommandInfo.ArgumentType.CustomArgument, CustomArgumentTemplate = "customArg", Description = "Custom argument"}]),
            ],
            CustomArgumentTemplates = [
                new () {
                    Name = "customArg",
                    Command = "cat testDir/customArgList"
                }
            ]
        };
        GetSuggestionsForTestExecutable(ci, " ");
        CustomArguments.CustomArgumentsLoaded += SetupWaitEvent().Invoke;
        WaitForEvent();

        var suggestions = GetSuggestionsForTestExecutable(ci, " ");
        Assert.That(suggestions, Does.Contain(new Suggestion("foo") {Description = "Custom argument"}));
        Assert.That(suggestions, Does.Contain(new Suggestion("bar") {Description = "Custom argument"}));
        Assert.That(suggestions, Does.Contain(new Suggestion("baz") {Description = "Custom argument"}));
    }
    
    [Test]
    public void CanGetSuggestionsForCustomArgumentWithRegex()
    {
        var testDir = new NPath("testDir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        testDir.Combine("customArgListWithRegex").WriteAllText("foo2;the foo argument\nbar bar;the bar argument\nbazz;the baz argument\n");

        var ci = new CommandInfo
        {
            Arguments = [
                new([ new("arg") { Type = CommandInfo.ArgumentType.CustomArgument, CustomArgumentTemplate = "regex", Description = "Custom argument with regex"}])
            ],
            CustomArgumentTemplates = [
                new () {
                    Name = "regex",
                    Command = "cat testDir/customArgListWithRegex",
                    Regex = "(.+);(.+)\n"
                }
            ]
        };
        
        GetSuggestionsForTestExecutable(ci, " ");
        CustomArguments.CustomArgumentsLoaded += SetupWaitEvent().Invoke;
        WaitForEvent();
        
        var suggestions = GetSuggestionsForTestExecutable(ci, " ");
        Assert.That(suggestions, Does.Contain(new Suggestion("foo2") {Description = "the foo argument"}));
        Assert.That(suggestions, Does.Contain(new Suggestion("bar bar") {Description = "the bar argument"}));
        Assert.That(suggestions, Does.Contain(new Suggestion("bazz") {Description = "the baz argument"}));
    }
    
    [Test]
    public void CanGetSuggestionsForCustomArgumentWithRegexWitMatchNames()
    {
        var testDir = new NPath("testDir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        testDir.Combine("customArgListWithRegex2").WriteAllText("foo2;the foo argument\nbar bar;the bar argument\nbazz;the baz argument\n");

        var ci = new CommandInfo
        {
            Arguments = [
                new([ new("arg") { Type = CommandInfo.ArgumentType.CustomArgument, CustomArgumentTemplate = "regex", Description = "Custom argument with regex"}]),
            ],
            CustomArgumentTemplates = [
                new () {
                    Name = "regex",
                    Command = "cat testDir/customArgListWithRegex2",
                    Regex = "(?<description>.+);(?<suggestion>.+)\n" 
                }
            ]
        };
        
        GetSuggestionsForTestExecutable(ci, " ");
        CustomArguments.CustomArgumentsLoaded += SetupWaitEvent().Invoke;
        WaitForEvent();
        
        var suggestions = GetSuggestionsForTestExecutable(ci, " ");
        Assert.That(suggestions, Does.Contain(new Suggestion("the foo argument") {Description = "foo2"}));
        Assert.That(suggestions, Does.Contain(new Suggestion("the bar argument") {Description = "bar bar"}));
        Assert.That(suggestions, Does.Contain(new Suggestion("the baz argument") {Description = "bazz"}));
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
            Arguments = [new([ new("") { Type = type }])]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " testDir/");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("testDir/directory/"));
        Assert.That(suggestions.Select(s => s.Text.Trim()).Contains("testDir/file"), Is.EqualTo(type != CommandInfo.ArgumentType.Directory));
    }

    [Test]
    public void FileSystemSuggestionsForPrivateFolderWontFail()
    {
        var testDir = new NPath("testDir").MakeAbsolute().CreateDirectory();
        Cli.Wrap("chmod").WithArguments("000 testDir").ExecuteBufferedAsync().GetAwaiter().GetResult();
        _pathsToCleanup.Add(testDir);

        var ci = new CommandInfo
        {
            Arguments = [new([ new("") { Type = CommandInfo.ArgumentType.FileSystemEntry }])]
        };
        Assert.DoesNotThrow(() => GetSuggestionsForTestExecutable(ci, " testDir/"));
    }
    
    [Test]
    public void CanDescribeFile()
    {
        var testDir = new NPath("testDir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        testDir.Combine("file").WriteAllText("This is a text file\n");

        var ci = new CommandInfo
        {
            Arguments = [new([ new("") { Type = CommandInfo.ArgumentType.File }])]
        };
        var fileDescriptionLoadedEvent = new ManualResetEvent(false);

        FileDescriptions.FileDescriptionLoaded += () =>
        {
            var suggestions = GetSuggestionsForTestExecutable(ci, " testDir/");
            Assert.That(suggestions[0].SecondaryDescription, Is.EqualTo("ASCII text"));
            fileDescriptionLoadedEvent.Set();
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " testDir/");
        _ = suggestions[0].SecondaryDescription;
        if (!fileDescriptionLoadedEvent.WaitOne(TimeSpan.FromSeconds(5)))
            Assert.Fail("Timeout: FileDescriptionLoaded event was not raised within the expected time.");
    }
    
    
    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public void SuggestionForFileSystemEntryHasDescription(bool fileExists)
    {
        var testDir = new NPath("testDir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        if (fileExists)
            testDir.Combine("file").WriteAllText("This is a text file\n");

        var ci = new CommandInfo
        {
            Arguments = [new([ new("") { Type = CommandInfo.ArgumentType.File, Description = "my file argument"}])]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " testDir/file");
        Assert.That(suggestions[0].Description, Is.EqualTo("my file argument"));
    }    
    
    
    [Test]
    public void SuggestionsForFileSystemEntriesAreEscaped()
    {
        var testDir = new NPath("testDir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        testDir.Combine("this is a directory").CreateDirectory();

        var ci = new CommandInfo
        {
            Arguments = [new([ new("") { Type = CommandInfo.ArgumentType.FileSystemEntry }])]
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
            Arguments = [new([ new("") { Type = CommandInfo.ArgumentType.FileSystemEntry }])]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " test\\ dir/");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("test\\ dir/test\\ sub\\ dir/"));
    }


    [Test]
    public void CanGetSuggestionsFromHistory()
    {
        History.LoadTestHistory(["foo", "bar", "foo bar"]);
        var suggestions = Suggestor.SuggestionsForPrompt("f");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("foo"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("foo bar"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Not.Contain("bar"));
    }
    
    [Test]
    public void CanGetSuggestionsFromHistoryWithEmptyCommandLine()
    {
        History.LoadTestHistory(["foo", "bar", "foo bar"]);
        var suggestions = Suggestor.SuggestionsForPrompt("");
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("foo"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("foo bar"));
        Assert.That(suggestions.Select(s => s.Text.Trim()), Does.Contain("bar"));
    }

    [Test]
    public void FileSystemArgumentIsSorted()
    {
        var testDir = new NPath("testDir").MakeAbsolute().CreateDirectory();
        _pathsToCleanup.Add(testDir);
        testDir.Combine("a_directory").CreateDirectory();
        testDir.Combine("a_file").WriteAllText("bla");
        testDir.Combine("b_directory").CreateDirectory();
        testDir.Combine("b_file").WriteAllText("bla");
        testDir.Combine(".invisibleDirectory").CreateDirectory();
        testDir.Combine(".invisibleFile").WriteAllText("bla");
        
        var ci = new CommandInfo
        {
            Arguments = [new([ 
                    new("") { Type = CommandInfo.ArgumentType.FileSystemEntry }
                ])]
        };
        
        // Desired sort order:
        // -Files from history come first, in the order used in history
        // Visible files or directories come before invisible (starting with .) ones
        // Directories before files
        // Then sorted alphabetically
        
        var suggestions = GetSuggestionsForTestExecutable(ci, " testDir/");
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new []
            {
                "testDir/a_directory/", 
                "testDir/b_directory/", 
                "testDir/a_file",
                "testDir/b_file",
                "testDir/.invisibleDirectory/",
                "testDir/.invisibleFile"
            }
        ));
        
        History.LoadTestHistory(["./testExecutable testDir/.invisibleDirectory/", "./testExecutable testDir/b_file"]);
        suggestions = GetSuggestionsForTestExecutable(ci, " testDir/");
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new [] 
            {
                "testDir/b_file",
                "testDir/.invisibleDirectory/",
                "testDir/a_directory/", 
                "testDir/b_directory/", 
                "testDir/a_file",
                "testDir/.invisibleFile"
            }
        ));
    }
    
    [Test]
    public void CanFilterExtensions()
    {
        var testDir = new NPath("testDir").MakeAbsolute().CreateDirectory();
        _pathsToCleanup.Add(testDir);
        testDir.Combine("file.a").WriteAllText("bla");
        testDir.Combine("file.b").WriteAllText("bla");
        testDir.Combine("file.c").WriteAllText("bla");
        
        var ci = new CommandInfo
        {
            Arguments = [new([ 
                    new("") { Type = CommandInfo.ArgumentType.File, Extensions = ["a", "c"]}
                ])]
        };
        
        var suggestions = GetSuggestionsForTestExecutable(ci, " testDir/");
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new []
            {
                "testDir/file.a", 
                "testDir/file.c", 
            }
        ));
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
            Arguments = [new([ new("") { Type = CommandInfo.ArgumentType.FileSystemEntry }])]
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
                new([
                    new("a") { Type = CommandInfo.ArgumentType.Flag },
                    new("b") { Type = CommandInfo.ArgumentType.Flag },
                    new("c") { Type = CommandInfo.ArgumentType.Flag },
                ])
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

    [Test] public void StringArgumentWillBeSuggestedFromHistory()
    {
        var ci = new CommandInfo
        {
            Arguments =
            [
                new([
                    new("string") { Type = CommandInfo.ArgumentType.String }
                ])
            ]
        };
        var suggestions = GetSuggestionsForTestExecutable(ci, " ");
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new [] {""}));
        
        History.LoadTestHistory(["./testExecutable foo"]);
        suggestions = GetSuggestionsForTestExecutable(ci, " ");
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new [] {"foo", ""}));
        
        suggestions = GetSuggestionsForTestExecutable(ci, " f");
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new [] {"foo", "f"}));

        // Make sure we only get one suggestion for 'foo'
        suggestions = GetSuggestionsForTestExecutable(ci, " foo");
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new [] {"foo"}));

        suggestions = GetSuggestionsForTestExecutable(ci, " b");
        Assert.That(suggestions.Select(s => s.Text.Trim()).ToArray(), Is.EqualTo(new [] {"b"}));

    }
    
    [Test]
    public void CanSuggestEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("FOO", "Test Value");
        var suggestions = Suggestor.SuggestionsForPrompt("$");
        Assert.That(suggestions, Does.Contain(new Suggestion("$FOO") {Description = "Test Value"}));
    }

    [Test]
    public void CanSuggestPathNameForVariableAssignment()
    {
        var testDir = new NPath("localTestDir").MakeAbsolute().CreateDirectory();   
        _pathsToCleanup.Add(testDir);
        var suggestions = Suggestor.SuggestionsForPrompt("FOO=");
        Assert.That(suggestions.Select(s => s.Text), Does.Contain("localTestDir/"));
    }
    
    [Test]
    public void SplitIntoCommandWordsHandlesEscapedSpace()
    {
        var split = Suggestor.SplitCommandIntoWords("This is a command\\ line with\\ escaped\\ spaces");
        Assert.That(split, Is.EqualTo(new [] {"This", "is", "a", "command\\ line", "with\\ escaped\\ spaces"}));
    }
    
    [Test]
    public void SplitIntoCommandWordsHandlesQuotes()
    {
        var split = Suggestor.SplitCommandIntoWords("This is a \"command line\" with quotes");
        Assert.That(split, Is.EqualTo(new [] {"This", "is", "a", "\"command line\"", "with", "quotes"}));
    }
    
    [Test]
    public void SplitIntoCommandWordsHandlesUnfinishedQuotes()
    {
        var split = Suggestor.SplitCommandIntoWords("This is a command line \"with quotes");
        Assert.That(split, Is.EqualTo(new [] {"This", "is", "a", "command", "line", "\"with quotes"}));
    }

    [Test]
    public void UnescapeFileNameHandlesEscapedCharacters()
    {
        Assert.That(Suggestor.UnescapeFileName(@"path\ name/with\ escaped\ spaces/and\\back\\slashes"), Is.EqualTo(@"path name/with escaped spaces/and\back\slashes"));
    }

    [Test]
    public void UnescapeFileNameHandlesUserDirs()
    {
        Assert.That(Suggestor.UnescapeFileName("~/dir/relative/to/current/user"), Is.EqualTo($"{NPath.HomeDirectory}/dir/relative/to/current/user"));
        Assert.That(Suggestor.UnescapeFileName("~root/dir/relative/to/root"), Is.EqualTo(RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ?"/var/root/dir/relative/to/root"
            :"/root/dir/relative/to/root")
        );
        Assert.That(Suggestor.UnescapeFileName($"~{Environment.GetEnvironmentVariable("USER")}/dir/relative/to/user/name"), Is.EqualTo($"{NPath.HomeDirectory}/dir/relative/to/user/name"));
    }
    
    private static void WaitForEvent()
    {
        if (!_eventToWaitFor.WaitOne(TimeSpan.FromSeconds(5)))
            Assert.Fail("Timeout: CustomArgumentsLoaded event was not raised within the expected time.");
    }
    
    [Test]
    public async Task GitIntegrationTest()
    {
        var testDir = NPath.CreateTempDirectory("testdir");
        var testFile = testDir.Combine("testFile").WriteAllText("Just a file");
        NPath.SetCurrentDirectory(testDir);

        Suggestor.SuggestionsForPrompt("git ");
        KnownCommands.CommandInfoLoaded += SetupWaitEvent<CommandInfo>(ci => ci.Name == "git").Invoke;
        WaitForEvent();

        var suggestions = Suggestor.SuggestionsForPrompt("git ");
        Assert.That(suggestions.Select(s => s.Text), Does.Contain("init"));

        await Cli.Wrap("git").WithArguments("init").ExecuteBufferedAsync();

        suggestions = Suggestor.SuggestionsForPrompt("git ");
        Assert.That(suggestions.Select(s => s.Text), Does.Contain("add"));

        Suggestor.SuggestionsForPrompt("git add ");

        CustomArguments.CustomArgumentsLoaded += SetupWaitEvent().Invoke;
        WaitForEvent();
        
        suggestions = Suggestor.SuggestionsForPrompt("git add ");
        Assert.That(suggestions.Select(s => s.Text), Does.Contain(testFile.FileName));

        NPath.SetCurrentDirectory(testDir.Parent);
    }
}