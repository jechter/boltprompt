# `boltprompt⚡️`

## tl;dr

boltprompt is a free tool to help you write shell commands in your terminal with auto-completion:

![boltprompt auto-completing git command lines.](https://jonasechterhoff.com/boltprompt.gif)

## Installation

boltprompt runs on macOS, and requires dotnet to be installed on your system. If you have brew, the install script will offer to install dotnet for you if you don't have it yet. boltprompt will also work on Linux, but is not particularly well tested there.

To install boltprompt, run:
```
curl https://raw.githubusercontent.com/jechter/boltprompt/refs/heads/main/install.sh | sh
```

Then, open a new Terminal window, and you should see the boltprompt command prompt.

If you are using iTerm2 or Apple's Terminal.app, you can set up your terminal with a font which works well for boltprompt using:

```
boltprompt setup-terminal
```

To enable AI features (which allows getting suggestions from human language requests, and to offer autocomplete suggestions for commands not known to boltprompt), you need an OpenAI API key (which requires paying small amounts of money to OpenAI). You can then turn on AI features using the following command. Read the disclaimer and type "ok" to confirm.

```
boltprompt config set OpenAiApiKey [your API key here]
```

If you want to uninstall boltprompt, run:

```
boltprompt uninstall
```

## Usage

### Automatic command line completion suggestions

When you start typing commands in your terminal, boltprompt will propose autocomplete suggestions for your commands. boltprompt will suggest commands, pathnames, environment variable names and arguments to known commands. It will also supply short descriptions of commands and arguments with it's suggestions. boltprompt suggestions are listed below the command prompt. You can select a suggestion with the up and down arrow keys, and commit a suggestion to the command line by hitting tab - or by hitting return, which will commit the selection, and immediately run the command. 

### AI command line suggestions

boltprompt can also propose suggested commands to run for human language requests. To use this feature, you need an OpenAI API key (which requires paying small amounts of money to OpenAI), and you need to supply your API key to boltprompt using `boltprompt config set OpenAiApiKey [your API key here]`. Then, you can write human language requests on the command line, by prefixing them with the _AI prompt prefix_ - which defaults to '@'. boltprompt will then use OpenAI GPT-4o mini LLM to generate suggested commands to perform your request. After a short delay of a second or so, you should see AI-generated suggestions show in the suggestion viewer.

Never execute suggested command lines if you don't understand what the commands do, as doing so may compromise your date and system security.
                              
By default, boltprompt will include personal information (including your OS, shell, installed commands, current directory listing, last run commands) in requests for command line suggestions sent to OpenAI. This improves the quality of suggestions. You can disable sharing of personal information by running the command `boltprompt config set RemovePersonalInformationFromAIQueries on`.                                

## Configuration

You can configure boltprompt options by typing `boltprompt config set ` into the boltprompt command prompt. boltprompt will then list all the available configuration options as autocomplete suggestions.

### Prompt Configuration

You can configure the prompt boltprompt prints before every command you type using `boltprompt config set Prompt newprompt`, where `newprompt` is a string to be used for the new prompt. Unicode characters can be escaped using `\uXXXX`. The prompt string may contain ANSI escape sequences to set color and style properties. For instance, `boltprompt config set Prompt "\u001b[38;5;1mredprompt\u2764\u001B[0m "` will give you a red prompt string with a heart character. The following sequences can be used to represent context sensitive strings, and will be replaced in the prompt string:

* `{timestamp}`: The current time
* `{host_name}`: Name of the current host
* `{user_name}`: Name of the current user
* `{working_directory_name}`: Name of the current working directory
* `{working_directory_short_path}`: Path of the current working directory, with all parent directories abbreviated by their first character
* `{working_directory_path}`: Full path of the current working directory
* `{prompt_symbol}`: The boltprompt symbol (⚡️) in most cases, but may be changed to identify an AI prompt (🤖), or a root prompt (☢️).

## Command description files

In order to be able to suggest arguments for commands, boltprompt needs command description files which describe the argument structure accepted by a command. boltprompt comes with description files for many common commands. Most of these are converted from [fig](https://github.com/withfig/autocomplete)'s (aka Amazon Q - a different command line autocomplete tool) "completion specs". However, the feature set of fig is not completely identical to the boltprompt feature set, so those converted specs often need manual adjustments. boltprompt still requires a lot of work in writing an improving description files.

If boltprompt does not have a command description file for a command, and you have configured boltprompt with an OpenAI API key, it will try to use AI to create one for the command. The quality of these AI-created command description files is usually not very good - but they can serve as a starting point for writing a good description file. These generated files will be written to `~/Library/Application Support/boltprompt/Commands/`.

If you are interested in writing your own description files and contributing to boltprompt, docs for this are coming soon.

