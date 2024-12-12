# Changelog

## [0.1.2]

### Added

- Can get questions in the context of the terminal answered by AI, by typing (by default) `? what is my question` and hitting return. Just typing `?` will try to explain what is going on in the last lines of the terminal.

### Changed

- Use Microsoft.AI instead of LangueModels library
- Always show suggestions when hitting tab, just don't select any by default.
- AI requests will include the last lines of the Terminal for context (supported in Terminal.app, iTerm2, or when using tmux).

## [0.1.1] - 2024-11-21

### Changed

- Better CommandInfos for many commands, resulting in better suggestions.
- Optimized performance for custom commands by caching output parsing.
- Don't automatically suggest subdirectories/files as a first suggestion when picking a directory, to avoid accidental selection of a subdirectory when hitting return.
- Make install script more reliable.
- Make history session specific

## [0.1.0] - 2024-11-14