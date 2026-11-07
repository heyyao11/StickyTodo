# Installs StickyTodo for the current user (no admin needed):
#   - copies the app to %LOCALAPPDATA%\Programs\StickyTodo
#   - adds a Start menu shortcut
#   - registers it to run at sign-in (shows in Task Manager > Startup apps)
#   - registers it in Settings > Apps so it can be uninstalled from there
# Your to-dos stay in %APPDATA%\StickyTodo and are not touched.
$ErrorActionPreference = 'Stop'
$src  = $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA 'Programs\StickyTodo'
$exe  = Join-Path $dest 'StickyTodo.exe'

# Close a running copy so the exe can be replaced
Get-Process StickyTodo -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $src 'StickyTodo.exe'), (Join-Path $src 'StickyTodo.ico'), (Join-Path $src 'uninstall.ps1') $dest -Force

# Start menu shortcut
$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Sticky Todo.lnk'
$sh = New-Object -ComObject WScript.Shell
$s = $sh.CreateShortcut($lnk)
$s.TargetPath = $exe
$s.WorkingDirectory = $dest
$s.IconLocation = "$exe,0"
$s.Description = 'Sticky Todo'
$s.Save()

# Run at sign-in (this is what Task Manager's Startup apps tab lists)
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'StickyTodo' -Value "`"$exe`""

# Settings > Apps > Installed apps entry
$un = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\StickyTodo'
New-Item $un -Force | Out-Null
$sizeKb = [int]((Get-ChildItem $dest | Measure-Object Length -Sum).Sum / 1KB)
$props = @{
    DisplayName     = 'Sticky Todo'
    DisplayVersion  = '1.0.0'
    Publisher       = 'Sticky Todo'
    DisplayIcon     = "$exe,0"
    InstallLocation = $dest
    UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$dest\uninstall.ps1`""
    InstallDate     = (Get-Date -Format 'yyyyMMdd')
}
foreach ($k in $props.Keys) { Set-ItemProperty $un -Name $k -Value $props[$k] }
Set-ItemProperty $un -Name NoModify -Value 1 -Type DWord
Set-ItemProperty $un -Name NoRepair -Value 1 -Type DWord
Set-ItemProperty $un -Name EstimatedSize -Value $sizeKb -Type DWord

Start-Process $exe
Write-Host "Installed to $dest and started Sticky Todo."
