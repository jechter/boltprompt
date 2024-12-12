on run argv
    set logfile to item 1 of argv
    tell application "Terminal"
        set output to (contents of selected tab of front window)
    end tell
    do shell script "echo " & quoted form of output & " > " & quoted form of logfile
end run