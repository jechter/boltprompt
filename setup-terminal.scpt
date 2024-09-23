tell application "Terminal"
	set ProfilesNames to name of every settings set
	repeat with ProfileName in ProfilesNames
		set font name of settings set ProfileName to "0xProto Nerd Font"
		set CleanCommands to clean commands of settings set ProfileName
		if CleanCommands does not contain "boltprompt" then
			set clean commands of settings set ProfileName to CleanCommands & "boltprompt"
		end if
	end repeat
end tell