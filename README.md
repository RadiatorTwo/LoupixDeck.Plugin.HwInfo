# LoupixDeck.Plugin.HwInfo

HWiNFO integration plugin for [LoupixDeck](https://github.com/RadiatorTwo/LoupixDeck),
built against [LoupixDeck.PluginSdk](https://github.com/RadiatorTwo/LoupixDeck.PluginSdk).

Windows only. Requires "Shared Memory Support" to be enabled in HWiNFO.

## Commands

`HwInfo.Sensor` — a display command that renders a chosen HWiNFO sensor
reading onto a touch button (updated every 2 s). Sensors are offered as a
live tree in the touch-button command menu.

## Build & deploy

```bash
dotnet build LoupixDeck.Plugin.HwInfo.csproj -c Release
```

Copy the build output together with `plugin.json` into
`LoupixDeck/plugins/hwinfo/`.
