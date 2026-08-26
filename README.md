<h1 align="center">
  <img width="384" src="EggLedger.Desktop/icon-512.png" alt="EggLedger">
</h1>

<p align="center">
  <a href="https://eggledger.davidarthurcole.me/"><img src="EggLedger.Desktop/assets/open-web-app.svg" alt="open web app"></a>
  <a href="https://github.com/EggIncTools/EggLedger/releases"><img src="EggLedger.Desktop/assets/download.svg" alt="download desktop"></a>
  <a href="https://discord.davidarthurcole.me"><img src="https://img.shields.io/badge/discord-join%20server-5865F2?logo=discord&logoColor=white" alt="Discord"></a>
</p>

**EggLedger** exports your Egg, Inc. spaceship mission history, including every loot drop, to `.xlsx` and `.csv` so you can slice it however you like. It extends the [rockets tracker](https://wasmegg-carpet.netlify.app/rockets-tracker/), answering the questions that tool can't: "which mission dropped this legendary?" and "how many of this item have I ever pulled?"

## Use it

**EggLedger as a Web app: [eggledger.davidarthurcole.me](https://eggledger.davidarthurcole.me/)**
- Nothing to install.
- Connect with Discord to start.
  - ONLY used for login/identification
  - Optionally can be used to store/sync settings
- Enter your EID, and pull your data.

This is the recommended way to run EggLedger.<br><br>

Prefer a local app?
**[Download the desktop build](https://github.com/EggIncTools/EggLedger/releases)** instead.
- Same features as the web app
- (Optionally) Connect to Discord to sync Settings.
- Runs offline, keeps everything on your machine.

## What's "new"

EggLedger was rewritten from Go + Vue to C#. The domain logic, decode, and reports math are validated against the original Go output with golden fixtures, so the numbers match what you had before. The rewrite brought:

- A hosted web app, so you no longer need to download anything to use it.
- A shared UI between web and desktop. Both run on the same Blazor code.
- Native AES-GCM and protobuf decode on every host. No browser engine shipped with the app.

## Security and privacy

**Is my data shared with anyone?**

No. EggLedger talks to the Egg, Inc. API and nothing else, save for an occasional update check against github.com. No analytics, no telemetry, no account info leaves your control. The developer has no way of knowing you even use the tool unless you say so.

**Is there any risk to my account?**

Nothing I'm aware of. The [rockets tracker](https://wasmegg-carpet.netlify.app/rockets-tracker/) has used the same techniques safely for years. That said, none of these tools are sanctioned by the Egg, Inc. developer. Use them at your own risk; I'm not responsible for any fallout.

## License

The MIT License. See COPYING.

## Contributing

Contributions welcome. Fork the repo, make your change, open a pull request.

## Development

EggLedger is a .NET 10 solution. Two hosts share one Razor Class Library (`EggLedger.Web`): the Blazor Server web app (`EggLedger.Web.Server`, the deployed image) and the Photino desktop app (`EggLedger.Desktop`). Pure domain logic lives in `EggLedger.Domain`.

```bash
dotnet build EggLedger.slnx
dotnet test EggLedger.slnx
dotnet publish EggLedger.Web.Server -c Release -o out
```