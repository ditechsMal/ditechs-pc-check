<#
.SYNOPSIS
    Installs (or removes) Ditechs PC Check on this PC.

.DESCRIPTION
    Per-machine install of the Ditechs PC Check desktop app:
      - copies the executable to Program Files
      - creates Start Menu and Desktop shortcuts
      - registers an entry in Apps & features (Add/Remove Programs)
      - can uninstall cleanly with -Uninstall

    This INSTALLER needs administrator rights, because it writes to Program Files.
    The TOOL itself never does: it only reads, and never asks for permission.

    Deployable silently to clients:
        powershell -NoProfile -ExecutionPolicy Bypass -File Install-DitechsPcCheck.ps1 -Quiet

.PARAMETER Uninstall
    Removes the app, its shortcuts and its Apps & features entry.

.PARAMETER SourcePath
    Path to DitechsPcCheck.exe. Defaults to the copy sitting next to this script.

.PARAMETER NoDesktopShortcut
    Skip creating the Desktop shortcut.

.PARAMETER Quiet
    No console output (use for scripted deployment).

.EXAMPLE
    .\Install-DitechsPcCheck.ps1

.EXAMPLE
    .\Install-DitechsPcCheck.ps1 -Uninstall
#>
[CmdletBinding()]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '',
    Justification = 'An interactive installer wants coloured console output; Write-Status honours -Quiet.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
    Justification = 'Runs non-interactively for deployment; the caller has chosen to install and Windows prompts for UAC.')]
param(
    [switch] $Uninstall,
    [string] $SourcePath,
    [switch] $NoDesktopShortcut,
    [switch] $Quiet
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- constants
$AppName     = 'Ditechs PC Check'
$Publisher   = 'Ditechs'
$Version     = '1.0.0'
$ExeName     = 'DitechsPcCheck.exe'
$AppId       = 'DitechsPcCheck'
$HelpLink    = 'https://ditechs.co.uk/tools/'
$ReqDotNet48 = 528040                      # Release value for .NET Framework 4.8

$InstallDir  = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) $AppId
$StartMenu   = Join-Path ([Environment]::GetFolderPath('Programs')) $AppName
$Desktop     = [Environment]::GetFolderPath('Desktop')
$UnrootFull  = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + $AppId
$UnrootWow   = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\' + $AppId
$SelfPath    = $PSCommandPath

# Single explicit read of the -Quiet switch; helpers below consult $script:IsQuiet
# rather than reaching for the parameter through dynamic scope.
$script:IsQuiet = [bool] $Quiet

function Write-Status {
    param([string] $Message, [string] $Colour = 'Gray')
    if (-not $script:IsQuiet) { Write-Host $Message -ForegroundColor $Colour }
}

function Test-Elevated {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Relaunch under 64-bit PowerShell, then elevated. Keeps the arguments intact.
function Restart-Self {
    param([string] $Reason)
    $psi = if ($Reason -eq 'bitness') {
        Join-Path $env:SystemRoot 'Sysnative\WindowsPowerShell\v1.0\powershell.exe'
    } else {
        Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    }

    $argv = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $SelfPath + '"'))
    if ($Uninstall)         { $argv += '-Uninstall' }
    if ($NoDesktopShortcut) { $argv += '-NoDesktopShortcut' }
    if ($script:IsQuiet)    { $argv += '-Quiet' }
    if ($SourcePath)        { $argv += @('-SourcePath', ('"' + $SourcePath + '"')) }

    if ($Reason -eq 'bitness') {
        Write-Status 'Re-launching under 64-bit PowerShell...' 'DarkGray'
        Start-Process -FilePath $psi -ArgumentList $argv -Wait
    } else {
        Write-Status 'Requesting administrator rights...' 'DarkGray'
        Start-Process -FilePath $psi -ArgumentList $argv -Verb RunAs -Wait
    }
    exit 0
}

function New-Shortcut {
    param([string] $LinkPath, [string] $Target, [string] $WorkingDir)
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($LinkPath)
    $lnk.TargetPath       = $Target
    $lnk.WorkingDirectory = $WorkingDir
    $lnk.IconLocation     = "$Target,0"
    $lnk.Description      = 'See how healthy this PC is, in plain English'
    $lnk.Save()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
}

function Get-DotNet48Release {
    $key = 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
    if (-not (Test-Path $key)) { return 0 }
    $value = (Get-ItemProperty -Path $key -Name Release -ErrorAction SilentlyContinue).Release
    if ($null -eq $value) { return 0 }
    [int] $value
}

# ------------------------------------------------------------------ checks
if (-not (Test-Elevated)) { Restart-Self -Reason 'elevation' }
# 64-bit OS but a 32-bit host would put us in the wrong registry/Program Files view.
if ([Environment]::Is64BitOperatingSystem -and -not [Environment]::Is64BitProcess) {
    Restart-Self -Reason 'bitness'
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw "$AppName requires 64-bit Windows."
}

# -------------------------------------------------------------- uninstall
if ($Uninstall) {
    Write-Status "Uninstalling $AppName..." 'Cyan'

    foreach ($root in @($UnrootFull, $UnrootWow)) {
        if (Test-Path $root) { Remove-Item -Path $root -Recurse -Force }
    }

    $startLnk   = Join-Path $StartMenu ('{0}.lnk' -f $AppName)
    $desktopLnk = Join-Path $Desktop   ('{0}.lnk' -f $AppName)
    foreach ($f in @($startLnk, $desktopLnk)) {
        if (Test-Path $f) { Remove-Item $f -Force }
    }

    if (Test-Path $StartMenu) { Remove-Item $StartMenu -Recurse -Force }

    # Last, because this script lives here.
    if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }

    Write-Status "$AppName has been removed." 'Green'
    exit 0
}

# ---------------------------------------------------------------- install
Write-Status "Installing $AppName $Version" 'Cyan'

# 1. .NET Framework 4.8 ships with Windows 10 (1903+) and 11, but check anyway.
$release = Get-DotNet48Release
if ($release -lt $ReqDotNet48) {
    throw ("$AppName needs Microsoft .NET Framework 4.8. " +
           "This PC reports release $release (4.8 is $ReqDotNet48). " +
           "Run Windows Update, then try again.")
}

# 2. Find the exe to install.
if (-not $SourcePath) {
    $here = if ($SelfPath) { Split-Path -Parent $SelfPath } else { (Get-Location).Path }
    $SourcePath = Join-Path $here $ExeName
}
if (-not (Test-Path $SourcePath)) {
    throw "Cannot find $ExeName. Expected it at: $SourcePath`nPass -SourcePath to point at it."
}
$SourcePath = (Resolve-Path $SourcePath).Path
Write-Status "  source : $SourcePath"

# 3. Copy the program files.
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}
$targets = @($ExeName, 'README.txt', 'Install-DitechsPcCheck.ps1')
foreach ($name in $targets) {
    $from = Join-Path (Split-Path -Parent $SourcePath) $name
    if (Test-Path $from) { Copy-Item -Path $from -Destination $InstallDir -Force }
}
Write-Status "  install: $InstallDir"

$exePath = Join-Path $InstallDir $ExeName

# 4. Shortcuts.
if (-not (Test-Path $StartMenu)) {
    New-Item -ItemType Directory -Path $StartMenu -Force | Out-Null
}
New-Shortcut -LinkPath (Join-Path $StartMenu ('{0}.lnk' -f $AppName)) -Target $exePath -WorkingDir $InstallDir
Write-Status "  start menu shortcut created"

if (-not $NoDesktopShortcut) {
    New-Shortcut -LinkPath (Join-Path $Desktop ('{0}.lnk' -f $AppName)) -Target $exePath -WorkingDir $InstallDir
    Write-Status "  desktop shortcut created"
}

# 5. Apps & features entry.
$sizeKb = [int] ((Get-Item $exePath).Length / 1KB)
$uninstallScript = Join-Path $InstallDir 'Install-DitechsPcCheck.ps1'
$uninstallCmd = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}" -Uninstall' -f $uninstallScript

if (-not (Test-Path $UnrootFull)) { New-Item -Path $UnrootFull -Force | Out-Null }
$props = [ordered]@{
    DisplayName     = $AppName
    DisplayVersion  = $Version
    Publisher       = $Publisher
    InstallLocation = $InstallDir
    InstallDate     = (Get-Date -Format 'yyyyMMdd')
    UninstallString = $uninstallCmd
    QuietUninstallString = ($uninstallCmd + ' -Quiet')
    DisplayIcon     = $exePath
    EstimatedSize   = $sizeKb
    HelpLink        = $HelpLink
    NoModify        = 1
    NoRepair        = 1
}
foreach ($k in $props.Keys) {
    $kind = 'String'
    if ($props[$k] -is [int]) { $kind = 'DWord' }
    New-ItemProperty -Path $UnrootFull -Name $k -Value $props[$k] -PropertyType $kind -Force | Out-Null
}

Write-Status ''
Write-Status "$AppName $Version installed." 'Green'
Write-Status "Launch it from the Start Menu, the Desktop, or: $exePath" 'Gray'
Write-Status 'Only this installer needed permission. The tool itself never asks for it - it only reads.' 'DarkGray'
