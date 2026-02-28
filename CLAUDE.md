# DevDesk

After making changes, publish the single-file binary:

```sh
cd src/DevDesk && dotnet publish -c Release -r win-x64 --self-contained
```

Output: `src/DevDesk/bin/Release/net10.0-windows/win-x64/publish/DevDesk.exe`
