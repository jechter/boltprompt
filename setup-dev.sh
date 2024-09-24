#!/bin/bash

script_dir=$(dirname "$(readlink -f "$0")")

dotnet build
export PATH=$script_dir/boltprompt/bin/Debug/net8.0:$PATH