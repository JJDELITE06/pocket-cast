#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
source_dir=(source/UxPlay-*)
if ! grep -q 'POCKETCAST_IPV4' "${source_dir[0]}/lib/mdnsd/mdnsd.c"; then
  patch -d "${source_dir[0]}" -p1 < PocketCast-USB-interface.patch
fi
cmake -S "${source_dir[0]}" -B "${source_dir[0]}/build" -G Ninja -DNO_MARCH_NATIVE=ON -DCMAKE_BUILD_TYPE=Release
cmake --build "${source_dir[0]}/build" --parallel 4
"${source_dir[0]}/build/uxplay.exe" -h
