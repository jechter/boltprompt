#!/bin/bash

TEMP_DIR=$(mktemp -d)

url="https://github.com/ryanoasis/nerd-fonts/releases/latest/download/0xProto.zip"
zipfile="$TEMP_DIR/0xProto.zip"
output_dir="$HOME/Library/Fonts"


curl -L -o "$zipfile" "$url"
unzip -o "$zipfile" -d "$TEMP_DIR/nerd-fonts-temp"
cp nerd-fonts-temp/*.ttf "$output_dir"
rm -rf "$TEMP_DIR"

case "$TERM_PROGRAM" in
  Apple_Terminal)
    osascript setup-terminal.scpt
    ;;
  iTerm.app)
	/usr/libexec/PlistBuddy -c "Set :\"New Bookmarks\":0:\"Normal Font\" \"0xProtoNFM-Regular 14\""  ~/Library/Preferences/com.googlecode.iterm2.plist
	/usr/libexec/PlistBuddy -c "Set :\"New Bookmarks\":0:\"Non Ascii Font\" \"0xProtoNFM-Regular 14\""  ~/Library/Preferences/com.googlecode.iterm2.plist
	/usr/libexec/PlistBuddy -c "Add :\"New Bookmarks\":0:\"Jobs to Ignore\": string boltprompt" ~/Library/Preferences/com.googlecode.iterm2.plist
    ;;
  *)
    echo "Unsupported terminal: $TERM_PROGRAM"
    exit 1
    ;;
esac