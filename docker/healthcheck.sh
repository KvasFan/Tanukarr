#!/bin/sh
set -eu

# This read-only process must not take a shared .NET file lock on config.xml:
# on Linux it would block the application's exclusive configuration writer.
# A concurrent partial XML write may fail this probe, never application startup.
export DOTNET_SYSTEM_IO_DISABLEFILELOCKING=1

# A health probe must not repeat startup ownership preparation or run as root.
if [ "$(id -u)" = "0" ]; then
    exec gosu "${PUID:-1000}:${PGID:-1000}" /app/Sonarr -healthcheck -data=/config
fi

exec /app/Sonarr -healthcheck -data=/config
