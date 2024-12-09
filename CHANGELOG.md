# Changelog

## [0.1.2]

## Changed

- Use Microsoft.AI instead of LangueModels library
- Always show suggestions when hitting tab, just don't select any by default.

## [0.1.1] - 2024-11-21

### Changed

- Better CommandInfos for many commands, resulting in better suggestions.
- Optimized performance for custom commands by caching output parsing.
- Don't automatically suggest subdirectories/files as a first suggestion when picking a directory, to avoid accidental selection of a subdirectory when hitting return.
- Make install script more reliable.
- Make history session specific

## [0.1.0] - 2024-11-14