#!/usr/bin/env $SHELL   

if [ -n "$TERM" ]; then
  trap signal_handler SIGINT
  
  signal_handler() {
      trap - SIGINT    
  }
  
  case "$SHELL" in
    */bash)
      SETUP_SCRIPT_PATH=$(readlink -f "${BASH_SOURCE[0]}")
      ;;
    */zsh)
      # we don't want to print errors from "eval" if wildcards don't match files
      setopt +o nomatch
      SETUP_SCRIPT_PATH=$(readlink -f "${(%):-%N}")
      ;;
    *)
      echo "Unsupported shell: $SHELL"
      exit 1
      ;;
  esac
  
  BOLTPROMPT_PATH=$(dirname "$(dirname "$SETUP_SCRIPT_PATH")")
  
  export PATH=$BOLTPROMPT_PATH:$PATH
  
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
  
  TEMP_RESULT=0
  
  restore_status() { return $TEMP_RESULT; }
  
  generate_command() {
    boltprompt
    if [ $? -ne 0 ]; then
      echo "failed"
      return 1 
    else    
      CUSTOM_PROMPT=$(cat /tmp/custom-command)
      trap signal_handler SIGINT
      restore_status
      eval $CUSTOM_PROMPT
      TEMP_RESULT=$?
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
fi