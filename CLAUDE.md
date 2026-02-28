# Projects

- **DevDesk** — Creates a new virtual desktop with Terminal + VS Code + Chrome pre-arranged.
- **DeskSwitch** — Ctrl+Alt+Space overlay to fuzzy-search, switch, create, and remove virtual desktops.

# Build

After making changes, publish the single-file binaries:

```sh
cd src/DevDesk && dotnet publish -c Release -r win-x64 --self-contained
cd src/DeskSwitch && dotnet publish -c Release -r win-x64 --self-contained
```

Output:
- `src/DevDesk/bin/Release/net10.0-windows/win-x64/publish/DevDesk.exe`
- `src/DeskSwitch/bin/Release/net10.0-windows/win-x64/publish/DeskSwitch.exe`
