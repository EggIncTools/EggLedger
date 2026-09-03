# EggLedger desktop packaging

Photino desktop host. Self-contained, single-file publish per RID: win-x64,
linux-x64, osx-x64, osx-arm64.

MinVer (wired in the repo-root `Directory.Build.props`) derives the version
from the nearest `git describe` tag (prefix `v`). The self-updater reads
`InformationalVersion` at runtime against the latest GitHub release, so bump
the version by pushing a `vX.Y.Z` tag, not by editing a project file.

The `EggLedger.Web` RCL's Razor/wwwroot content is zipped into
`EggLedger.Desktop.wwwroot.zip` as an embedded resource at build time
(`ComposeWwwrootZip` target) and extracted to disk on first run, rather than
published as loose static web assets.

## Publish commands

Run from the repo root:

```bash
dotnet publish EggLedger.Desktop/EggLedger.Desktop.csproj -c Release -r win-x64 --self-contained -o dist/win-x64
dotnet publish EggLedger.Desktop/EggLedger.Desktop.csproj -c Release -r linux-x64 --self-contained -o dist/linux-x64
dotnet publish EggLedger.Desktop/EggLedger.Desktop.csproj -c Release -r osx-x64 --self-contained -o dist/osx-x64
dotnet publish EggLedger.Desktop/EggLedger.Desktop.csproj -c Release -r osx-arm64 --self-contained -o dist/osx-arm64
```

The csproj sets `RuntimeIdentifiers`, `SelfContained`, and `PublishSingleFile`;
add `-p:PublishSingleFile=false` for a loose output tree instead.
