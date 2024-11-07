# `boltprompt⚡️`

## tl;dr

boltprompt is a tool to help you write shell commands in your terminal with auto-completion:

![boltprompt auto-completing git command lines.](media/completion.gif)

It can also suggest commands from human language requests:

![boltprompt suggesting command lines.](media/ai.gif)

## Installation

Prerequisites:

* a Mac.
* dotnet is installed (`brew install dotnet`)

```
curl https://raw.githubusercontent.com/jechter/boltprompt/refs/heads/main/install.sh | sh
```

Open a new Terminal window

If you are using Apple's Terminal.app, you can set it up with a font which works well for boltprompt using:

```
boltprompt setup-terminal
```

Optional, to get suggestions from human language requests using "@", and to try to generate auto complete info for unknown commands using AI:

```
boltprompt config set OpenAiApiKey [your API key here]
```

If you want to get back to "normal":

```
boltprompt uninstall
```

