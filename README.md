# GPNVG18Thermal (GPNVG-18 Thermal)

A new pair of goggles: it looks like the **GPNVG-18** (model, four tubes, flip mechanism) but shows the **thermal image of the T-7**.

## How this mod was made (AI disclosure)

This mod was **programmed by Claude**, an AI assistant made by Anthropic.
**Kaan was the tester**: Kaan has no programming background, played with the mod
on SPT 4.1.6, reported what went wrong, and Claude fixed it. No human programmer
has reviewed the code. It is open source so anyone can check it — if you find a
bug, please open an [issue](https://github.com/Kaanori/spt-gpnvg18thermal/issues).

## Requirements

- SPT 4.1.6 (server).
- Optional: [Borkel's Realistic Night Vision Goggles](https://sp-mod.com/mod/954) – the ZIP contains a BorkelRNVG entry (lens image used with Borkel's permission).

## Installation

1. Download `GPNVG18Thermal-1.0.0.zip` from the [Releases](https://github.com/Kaanori/spt-gpnvg18thermal/releases) page.
2. Extract it **into your SPT game folder** (the folder that contains `EscapeFromTarkov.exe`).
   The ZIP already has the right layout; the mod ends up in `SPT_Runtime/user/mods/GPNVG18Thermal/`.
   On Windows, make sure "Extract All" does not add an extra subfolder.
3. Restart the SPT server.

## Uninstall

Delete the mod's files from `SPT_Runtime/user/mods/GPNVG18Thermal/` (settings: `BepInEx/config/` for client mods) and restart the SPT server.

## How it works

Whether the client draws night vision or a thermal image depends only on the parent node of the template. So the T-7 is cloned (which keeps all thermal values) and only the appearance of the GPNVG-18 is applied on top: model, mask, weight, conflicts. The new ID is added everywhere the GPNVG-18 fits, including helmets from other mods (WTT & co.) that load later.

## Parts

| Folder | What |
|---|---|
| `src/` | Server mod (creates the goggles), settings in `config.json` |
| `Client/` | Client plugin: the GPNVG-18 model insists on a night-vision component. Without the patch it is missing and the whole helmet is not built. The plugin attaches the tube light to the switch of the thermal component. |
| `BorkelRNVG/` | Entry for **BorkelRNVG** (own folder, category "T-7 Thermal", GPNVG mask) |

Goes well with [WaermebildVisier](https://github.com/Kaanori/spt-waermebildvisier) (thermal scope).

## Credits

- `BorkelRNVG/Thermal/gpnvg18_thermal/lens.png` is an unchanged copy of the T-7 lens from **Borkel's Realistic Night Vision Goggles** (https://sp-mod.com/mod/954), included with Borkel's permission ([GitHub](https://github.com/Borkel/RealisticNVG-client-2/issues/10)). All credit for it goes to Borkel. The `mask.png` was made for this mod.
- The BorkelRNVG entry is optional: without BorkelRNVG the goggles still work with the game's own thermal view.

## Building from source

Needs the .NET 10 SDK and an SPT 4.1.6 installation to build against.

```bash
./build.sh             # Linux: build only
./build.sh --install   # build and copy into the game folder
```
```powershell
.\build.ps1            # Windows
.\build.ps1 -Install
```

The game folder is set in `Directory.Build.props` (Windows default `C:/SPT`)
or via the environment variable `SPT_DIR`.

## Support

Bugs and questions: [GitHub issues](https://github.com/Kaanori/spt-gpnvg18thermal/issues).

## License

MIT — see [LICENSE](LICENSE).
