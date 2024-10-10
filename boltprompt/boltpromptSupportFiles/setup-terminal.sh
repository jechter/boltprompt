#!/bin/bash

TEMP_DIR=$(mktemp -d)

url="https://github.com/ryanoasis/nerd-fonts/releases/latest/download/0xProto.zip"
zipfile="$TEMP_DIR/0xProto.zip"
output_dir="$HOME/Library/Fonts"


curl -L -o "$zipfile" "$url"
unzip -o "$zipfile" -d "$TEMP_DIR/nerd-fonts-temp"
cp nerd-fonts-temp/*.ttf "$output_dir"
rm -rf "$TEMP_DIR"

osascript setup-terminal.scpt