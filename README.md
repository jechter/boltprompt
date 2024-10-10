# boltprompt

## tl;dr

boltprompt is a tool to help you write shell commands in your terminal with auto-completion.

## Installation

Prerequisites:

* a Mac.
* dotnet is installed (`brew install dotnet`)

```
git clone --recurse-submodules https://github.com/jechter/boltprompt.git
cd boltprompt
dotnet build
boltprompt/bin/Release/net8.0/boltprompt install
```

Open a new Terminal window

If you are using Apple's Terminal.app, you can set it up with a font which works well for boltprompt using:

```
boltprompt setup-terminal
```

Optional, to try to generate auto complete info for unknown commands using AI:

```
export OPENAI_API_KEY=[your API key here]
```

