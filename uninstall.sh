#!/bin/bash

exit_if_tool_does_not_exist()
{
	if ! command -v "$1" &> /dev/null; then
		echo "$1 could not be found. Please install $1."
		exit 1
	fi
}

exit_if_tool_does_not_exist "dotnet"

line_to_remove='source ~/.dotnet/tools/.store/boltprompt/1.0.0-beta/boltprompt/1.0.0-beta/tools/setup_boltprompt.sh'

remove_line_if_exists() {
  local file="$1"
  local line="$2"

  escaped_line=$(echo "$line" | sed 's/[^^]/[&]/g; s/\^/\\^/g')

  # For macOS, use the `-i ''` syntax to edit in place without a backup
  if [[ "$OSTYPE" == "darwin"* ]]; then
    sed -i '' "/$escaped_line/d" "$file"
  else
    sed -i "/$escaped_line/d" "$file"
  fi
}

# Detect the current shell
case "$SHELL" in
  */bash)
    remove_line_if_exists "$HOME/.bash_profile" "$line_to_remove"
    remove_line_if_exists "$HOME/.bashrc" "$line_to_remove"
    ;;
  */zsh)
    remove_line_if_exists "$HOME/.zshrc" "$line_to_remove"
    ;;
  *)
    echo "Unsupported shell: $SHELL"
    exit 1
    ;;
esac

dotnet tool uninstall -g boltprompt || true