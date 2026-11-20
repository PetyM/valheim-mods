# Valheim módy

## LevelEasily
Položka motyky „Level area“: srovná obdélník jedním kliknutím za cenu ruční práce. Mód je celý anglicky,
dokumentace v [LevelEasily/README.md](LevelEasily/README.md) a specifikace v [LevelEasily/SPEC.md](LevelEasily/SPEC.md).

## RecipeBrowser
Prohlížeč receptů ve stylu JEI (potřebuje Jotunn, jen klient). Mód je celý anglicky, dokumentace v [RecipeBrowser/README.md](RecipeBrowser/README.md).

## Build
```
cd LevelEasily && dotnet build      # nebo RecipeBrowser
```
Jiná herní složka: `dotnet build -p:ValheimDir=/cesta/k/Valheim`.

## Vydání na Thunderstore
Tým `PetyM`, zdrojáky na GitHubu `PetyM/valheim-mody`, licence MIT.
1. Zvednout verzi v `[BepInPlugin]` v `Plugin.cs` a v `Thunderstore/manifest.json` (build zkontroluje, že sedí)
   a přidat záznam do `CHANGELOG.md`.
2. `dotnet build -c Release` ve složce módu vyrobí `build/PetyM-<Mód>-<verze>.zip`
   (manifest, ikona, README a CHANGELOG módu, DLL v `plugins/`).
3. Nahrát na https://thunderstore.io/package/create/ (komunita Valheim). README módu je stránka pro hráče,
   relativní odkazy v něm nefungují, používej plné URL na GitHub.

Ikony: `<Mód>/Thunderstore/icon.svg` je zdroj, `icon.png` (256×256) se renderuje `rsvg-convert -w 256 -h 256`.

## Vazby mimo repo
- Build čte DLL ze Steam složky Valheimu (`~/.local/share/Steam/steamapps/common/Valheim`) a zapisuje do jejích
  `BepInEx/plugins/<Mód>/`.
- `dotnet-sdk` z pacmanu, `ilspycmd` jako globální dotnet tool (`~/.dotnet/tools`).
