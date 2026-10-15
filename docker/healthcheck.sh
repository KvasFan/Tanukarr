#!/bin/sh
set -eu

# A health probe must not repeat startup ownership preparation or run as root.
if [ "$(id -u)" = "0" ]; then
    exec gosu "${PUID:-1000}:${PGID:-1000}" /app/Sonarr -healthcheck -data=/config
fi

exec /app/Sonarr -healthcheck -data=/config
