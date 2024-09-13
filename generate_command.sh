#!/bin/bash

trap signal_handler SIGINT SIGTERM

signal_handler() {
    run
}

# Function to generate a custom command
generate_command() {
    /Users/jechter/Shelper/Shelper/Shelper/bin/Debug/net8.0/Shelper
    
    CUSTOM_PROMPT=$(cat /tmp/custom-command)
    # Execute the custom command
    eval $CUSTOM_PROMPT
}

run() {
	while true; do
	# Call the function to generate and run the custom command
		generate_command
	done
}

run