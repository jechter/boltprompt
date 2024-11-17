#!/bin/sh

exit_if_tool_does_not_exist() {
	if ! (command -v "$1" &> /dev/null); then
		echo "$1 could not be found."

		if (command -v brew &> /dev/null); then
			read -p "Would you like to install $1 using brew? (y/n): " choice
			if [[ "$choice" == "y" || "$choice" == "Y" ]]; then
				brew install "$1"
				if [ $? -ne 0 ]; then
					echo "Failed to install $1."
					exit 1
				fi
				echo "$1 has been installed."
				return
			else
				echo "Please install $1 manually."
				echo $2
				exit 1
			fi
		else
			echo "Please install $1 manually."
			echo $2
			exit 1
		fi
	fi
}

exit_if_tool_does_not_exist "git"
exit_if_tool_does_not_exist "dotnet" "Go to https://dotnet.microsoft.com/en-us/download/dotnet/8.0 to download dotnet"


oldpwd=$(pwd)
boltprompt_src=$(mktemp -d ${TMPDIR:-/tmp}/boltprompt_src.XXXXXX)
cd $boltprompt_src
git clone --branch release --recurse-submodules https://github.com/jechter/boltprompt.git
cd boltprompt
dotnet publish --output ~/.boltprompt boltprompt/boltprompt.csproj
~/.boltprompt/boltprompt install

cd $oldpwd
rm -rf $boltprompt_src