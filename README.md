# NullCore

NullCore is a personal Roblox bootstrapper/fork for T1nkq and family use.

This repository is wired to its own GitHub releases:

- Releases: https://github.com/T1nkq/NullCore/releases
- Latest build: https://github.com/T1nkq/NullCore/releases/latest
- Issues: https://github.com/T1nkq/NullCore/issues

## Notes

- Windows 10 and newer are supported.
- The app does not inject cheats, exploits, or bypass Roblox security.
- Release builds should be uploaded to this repository's GitHub Releases so the in-app updater can find them.

## Development

Open `NullCore.sln` in Visual Studio or build from the command line:

```powershell
dotnet build .\NullCore.sln
```

Publish a local x64 build:

```powershell
dotnet publish .\Bloxstrap\NullCore.csproj -c Release -r win-x64 --self-contained true
```

## Credits And Licenses

NullCore is based on Voidstrap/Bloxstrap-family code and keeps the upstream license files in the repository:

- `LICENSE`
- `LICENSE.VOIDSTRAP`
- `LICENSE.BLOXSTRAP`
- `LICENSE.FISHSTRAP`
