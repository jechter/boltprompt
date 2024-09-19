#!/bin/bash

script_dir=$(dirname $0)

cd "$script_dir/autocomplete"
pnpm install
pnpm build
cp -R build ../boltprompt/boltpromptSupportFiles/autocomplete