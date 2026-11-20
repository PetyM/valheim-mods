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

## Vazby mimo repo
- Build čte DLL ze Steam složky Valheimu (`~/.local/share/Steam/steamapps/common/Valheim`) a zapisuje do jejích
  `BepInEx/plugins/<Mód>/`.
- `dotnet-sdk` z pacmanu, `ilspycmd` jako globální dotnet tool (`~/.dotnet/tools`).
