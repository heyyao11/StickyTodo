# Removes the StickyTodo install made by install.ps1.
# Your to-dos in %APPDATA%\StickyTodo are kept.
$dest = Join-Path $env:LOCALAPPDATA 'Programs\StickyTodo'

Get-Process StickyTodo -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'StickyTodo' -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\StickyTodo' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Sticky Todo.lnk') -Force -ErrorAction SilentlyContinue

# This script lives in the folder it deletes, so remove the folder after it exits
Start-Process cmd.exe -WindowStyle Hidden -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q `"$dest`""
Write-Host 'Sticky Todo uninstalled (your to-dos in %APPDATA%\StickyTodo were kept).'
