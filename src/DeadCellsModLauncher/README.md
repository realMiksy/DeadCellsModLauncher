# Dead Cells Mod Launcher source

WPF/.NET 8 source for the Dead Cells Multiplayer Mod launcher.

Build locally:

```powershell
dotnet publish DeadCellsMultiplayerModInstaller.csproj -c Release -r win-x64 --self-contained true
```

The root GitHub Actions workflow builds a single-file `DeadCellsModLauncher.exe` automatically.
