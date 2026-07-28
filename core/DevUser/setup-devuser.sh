#!/usr/bin/env bash
#
# Creates the static plaintorchdev system account used by interactive/manual `serve` during development and testing.
#
# Interactive `serve` relaunches itself via `sudo -u plaintorchdev` so dev/test sessions never touch the real user's
# ~/.pleiades environment. End users run PLAINTORCH through the installed systemd service and never need this account.
#
# Run once with privileges to create system accounts. Idempotent. Ensure your dev user is permitted to
# `sudo -u plaintorchdev` (a sudoers rule) so the relaunch does not prompt in non-interactive sessions.
set -euo pipefail

USER_NAME="plaintorchdev"

if id "$USER_NAME" >/dev/null 2>&1; then
    echo "Account '$USER_NAME' already exists."
else
    sudo useradd --system --create-home --shell /usr/sbin/nologin "$USER_NAME"
    echo "Created system account '$USER_NAME'."
fi

echo "Done. Interactive 'serve' will relaunch via: sudo -u $USER_NAME <plaintorch> serve ..."
echo "Use 'serve --ephemeral' for throwaway/test environments."
