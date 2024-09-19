#!/usr/bin/env $SHELL   

trap signal_handler SIGINT SIGTERM

signal_handler() {
    run
}

export PATH=$HOME/.dotnet/tools:$PATH

add_to_history() {
  case "$SHELL" in
    */bash)
      history -s $1
      ;;
    */zsh)
      print -s "$1"
      ;;
    *)
      echo "Unsupported shell: $SHELL"
      exit 1
      ;;
  esac
}

generate_command() {
    boltprompt
    
    CUSTOM_PROMPT=$(cat /tmp/custom-command)
    eval $CUSTOM_PROMPT
    add_to_history $CUSTOM_PROMPT
}

run() {
	while true; do
		generate_command
	done
}

run