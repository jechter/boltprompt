on run argv
    set logfile to item 1 of argv
    tell application "iTerm"
        set output to (contents of current session of current window)
    end tell
    do shell script "echo " & quoted form of output & " > " & quoted form of logfile
end run