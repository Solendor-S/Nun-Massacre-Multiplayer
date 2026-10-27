# NunMP installer: double-click install.bat, or:  powershell -ExecutionPolicy Bypass -File install.ps1
param(
    [string]$Game = "",
    [string]$Pdm = ""
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$expected = "50FF7ED4B6BC7573A94E097EF34705DDCC4660A0"   # itch Definitive Edition 2022.06.08

function Pick-Folder($prompt) {
    Add-Type -AssemblyName System.Windows.Forms
    $d = New-Object System.Windows.Forms.FolderBrowserDialog
    $d.Description = $prompt
    if ($d.ShowDialog() -eq "OK") { return $d.SelectedPath } else { return "" }
}

if (-not $Game) { $Game = Pick-Folder "Select your Nun Massacre folder (the one with 'Nun Massacre.exe')" }
if (-not (Test-Path (Join-Path $Game "Nun Massacre.exe"))) { throw "Nun Massacre.exe not found in '$Game'" }

$asm = Join-Path $Game "Nun Massacre_Data\Managed\Assembly-CSharp.dll"
$hash = (Get-FileHash $asm -Algorithm SHA1).Hash
if ($hash -ne $expected) {
    Write-Warning "Your game build differs from the host's (itch Definitive Edition 2022.06.08). Multiplayer will refuse to connect."
    if ((Read-Host "Install anyway? (y/n)") -ne "y") { exit 1 }
}

Write-Host "Installing BepInEx + NunMP into $Game"
Copy-Item -Recurse -Force (Join-Path $here "game\*") $Game

# Optional: friends look like Power Drill Massacre's survivor if you have PDM (free demo on itch).
if (-not $Pdm) {
    if ((Read-Host "Do you have Power Drill Massacre 0.04 installed for the survivor model? (y/n)") -eq "y") {
        $Pdm = Pick-Folder "Select your Power Drill Massacre folder (with 'Power Drill Massacre.exe')"
    }
}
if ($Pdm) {
    $data = Join-Path $Pdm "Power Drill Massacre_Data"
    if (-not (Test-Path $data)) { throw "Power Drill Massacre_Data not found in '$Pdm'" }
    $py = Get-Command py -ErrorAction SilentlyContinue
    if (-not $py) { $py = Get-Command python -ErrorAction SilentlyContinue }
    if (-not $py) {
        Write-Warning "Python not found (install from python.org, tick 'Add to PATH'), then run this again. Skipping the survivor model."
    } else {
        $venv = Join-Path $env:TEMP "nunmp-venv"
        & $py.Source -m venv $venv
        & "$venv\Scripts\pip.exe" install -q UnityPy
        & "$venv\Scripts\python.exe" -I (Join-Path $here "pdm_export.py") $data (Join-Path $Game "BepInEx\plugins\NunMP\pdm")
    }
}

Write-Host ""
Write-Host "Done. Start Steam (logged in), then launch Nun Massacre. Press F2 on the main menu for the multiplayer panel." -ForegroundColor Green
Read-Host "Press Enter to close"
