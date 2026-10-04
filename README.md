# LoupixDeck.Plugin.HwInfo

HWiNFO integration plugin for [LoupixDeck](https://github.com/RadiatorTwo/LoupixDeck),
built against [LoupixDeck.PluginSdk](https://github.com/RadiatorTwo/LoupixDeck.PluginSdk).

Windows only. Requires "Shared Memory Support" to be enabled in HWiNFO. When HWiNFO is not
running, the tiles show "NOT RUNNING" and recover automatically once it is back. The plugin
reports this as an unmet requirement: the LoupixDeck Plugins page marks it "Needs attention"
and "Show Status" in the plugin settings says why.

## Features

Both commands draw pixel tiles (5×7 bitmap font, no anti-aliasing) with a gauge bar and a
72-second history chart. Readings turn amber or red past their limits (CPU relative to the
**CPU TjMax** setting, GPU core, drives, RAM load, a stalled fan while its temperature is high).
The **Transparent background** setting lets the page wallpaper show through.

Settings: transparent background, the CPU's TjMax (CPU temperature turns amber at
TjMax − 15 °C and red at TjMax − 5 °C), temperatures in °F instead of °C (display only), and
the alert limits: GPU 80/88 °C, drives 55/65 °C, RAM load 85/95 % (warning/critical) and a
stalled fan below 200 RPM by default.

- `HwInfo.Sensor` — one reading per command. Chain up to four on one button for a multi-row
  tile. Readings are offered as a live menu sorted by component (CPU, GPU, Memory, Storage,
  Mainboard, Network, Other), device and quantity, named after HWiNFO's English labels whatever
  language HWiNFO runs in. Buttons saved with earlier versions keep their reading.
- `HwInfo.Pages` — component pages CPU, GPU, RAM, NET, DISK, PWR, VRAM, BAT and a CPU summary;
  a key press shows the next page. The menu's "All pages" entry cycles through every page,
  including pages added later. Chain several `Pages` commands to build your own cycle. Pages
  without data are skipped. NET follows the adapter that carried the most data, DISK sums the
  transfer rates of all drives. PWR shows CPU package power, GPU board power and their sum; VRAM
  the primary GPU's memory in use; BAT the battery charge level (laptops).

The menu, the settings and the command texts are available in English, German and Spanish.
Requires LoupixDeck with Plugin SDK 1.28 or later.

## Build & deploy

```bash
dotnet build LoupixDeck.Plugin.HwInfo.csproj -c Release
```

Copy the build output together with `plugin.json` into
`LoupixDeck/plugins/hwinfo/`.
