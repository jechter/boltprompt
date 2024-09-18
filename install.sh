#!/bin/bash

exit_if_tool_does_not_exist()
{
	if ! command -v "$1" &> /dev/null; then
		echo "$1 could not be found. Please install $1."
		exit 1
	fi
}

exit_if_tool_does_not_exist "dotnet"
exit_if_tool_does_not_exist "pnpm"

./build-autocomplete.sh

dotnet pack --configuration Release
dotnet tool uninstall -g shelper || true
dotnet tool install -g shelper --add-source Shelper/bin/Release/ --version 1.0.0-beta 

line_to_add='source ~/.dotnet/tools/.store/shelper/1.0.0-beta/shelper/1.0.0-beta/tools/generate_command.sh'

add_line_if_not_exists() {
  local file="$1"
  local line="$2"

  if ! grep -qF "$line" "$file"; then
    echo "$line" >> "$file"
  fi
}

# Detect the current shell
case "$SHELL" in
  */bash)
    add_line_if_not_exists "$HOME/.bash_profile" "$line_to_add"
    add_line_if_not_exists "$HOME/.bashrc" "$line_to_add"
    ;;
  */zsh)
    add_line_if_not_exists "$HOME/.zshrc" "$line_to_add"
    ;;
  *)
    echo "Unsupported shell: $SHELL"
    exit 1
    ;;
esac
