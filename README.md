<h1 align="center">
  <img width="384" src="EggLedger.Desktop/icon-512.png" alt="EggLedger">
</h1>

<p align="center">
  <a href="https://eggledger.davidarthurcole.me/"><img src="EggLedger.Desktop/assets/open-web-app.svg" alt="open web app"></a>
  <a href="https://github.com/EggIncTools/EggLedger/releases"><img src="EggLedger.Desktop/assets/download.svg" alt="download desktop"></a>
  <a href="https://discord.davidarthurcole.me"><img src="https://img.shields.io/badge/discord-join%20server-5865F2?logo=discord&logoColor=white" alt="Discord"></a>
</p>

**EggLedger** exports your Egg, Inc. spaceship mission history, including every loot drop, to `.xlsx` and `.csv`. It extends the [rockets tracker](https://wasmegg-carpet.netlify.app/rockets-tracker/), answering questions that tool can't: "which mission dropped this legendary?" and "how many of this item have I ever pulled?"

## Use it

**Web app: [eggledger.davidarthurcole.me](https://eggledger.davidarthurcole.me/)** - nothing to install. Discord login is used only for identification, and optionally to sync settings.

**[Desktop build](https://github.com/EggIncTools/EggLedger/releases)** - same features, runs offline, keeps data on your machine.

## Privacy

EggLedger talks only to the Egg, Inc. API and an occasional update check against github.com. No analytics, no telemetry, no account data leaves your machine. Uses the same techniques the rockets tracker has used for years; not sanctioned by the Egg, Inc. developer, use at your own risk.

## License

The MIT License. See COPYING.

## Development

EggLedger is a .NET 10 solution. Two hosts share one Razor Class Library (`EggLedger.Web`): the Blazor Server web app (`EggLedger.Web.Server`, the deployed image) and the Photino desktop app (`EggLedger.Desktop`). Pure domain logic lives in `EggLedger.Domain`.

```bash
dotnet build EggLedger.slnx
dotnet test EggLedger.slnx
dotnet publish EggLedger.Web.Server -c Release -o out
```