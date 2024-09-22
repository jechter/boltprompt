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
    if [ $? -ne 0 ]; then
      echo "boltprompt failed!"
      return 1 
    else    
      CUSTOM_PROMPT=$(cat /tmp/custom-command)
      eval $CUSTOM_PROMPT
      add_to_history $CUSTOM_PROMPT
      return 0
    fi
}

run() {
	while generate_command; do
	  true
	done
}

run