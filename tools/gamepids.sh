#!/usr/bin/env bash
# Print PIDs of Nun Massacre processes running from the two LAB copies only (C:\Games\NunMassacre-MP / -MP2).
# Never by window title (browsers/editors) and never the user's own install, which may be running during tests.
powershell -NoProfile -Command "Get-Process -Name 'Nun Massacre' -ErrorAction SilentlyContinue | Where-Object { \$_.Path -like 'C:\Games\NunMassacre-MP\*' -or \$_.Path -like 'C:\Games\NunMassacre-MP2\*' } | ForEach-Object { \$_.Id }" | tr -d '\r'
