# Sticky Todo

A minimalist to-do sticky note for Windows 10/11 that can stay always on top, or tuck itself away at the edge of the screen.

![Sticky Todo demo: adding a to-do, ticking one off, marking one urgent, switching to the Sticky theme, then turning on auto-hide so the note slides into a tab at the screen edge and back out on hover](docs/demo.gif)

- **Auto-hide:** dock the note to any screen edge. It slides away into a thin tab when the mouse leaves and slides back when you hover the tab.
- **Always on top:** keep it visible over other windows.
- **Urgent items, notes and reminders:** right-click a to-do for more options.
- **Themes:** Light, Dark and Sticky.
- **Global shortcut:** show or hide the note from anywhere.

## Install

1. Open the [latest release](../../releases/latest) and download **StickyTodo-Setup.exe**.
2. Run it. It installs for your user only (no admin prompt) and adds a Start menu entry.
3. If Windows shows "Windows protected your PC", click **More info → Run anyway**. The app isn't code-signed.

Your to-dos are stored in `%APPDATA%\StickyTodo`. Uninstall from **Settings → Apps**.

## Build from source

Requires only the .NET Framework 4.8 compiler that ships with Windows.

```
build.cmd                  # builds StickyTodo.exe
ISCC.exe StickyTodo.iss    # builds dist\StickyTodo-Setup.exe (needs Inno Setup 6)
```

## Releasing

Push a version tag and GitHub Actions builds the installer and publishes the release:

```
git tag v1.2.0
git push origin v1.2.0
```
