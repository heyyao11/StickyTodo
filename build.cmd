@echo off
rem Builds StickyTodo.exe with its icon embedded. Close the running app first.
setlocal
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

rem 1. Compile once, then let the app write its own icon (same drawing as the tray icon)
"%CSC%" /nologo /target:winexe /optimize+ /out:StickyTodo.exe StickyTodo.cs || exit /b 1
start "" /wait StickyTodo.exe --write-icon StickyTodo.ico
if not exist StickyTodo.ico exit /b 1

rem 2. Compile again with the icon embedded
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:StickyTodo.ico /out:StickyTodo.exe StickyTodo.cs || exit /b 1
echo Built StickyTodo.exe
