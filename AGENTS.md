# Valheim módy

Vlastní BepInEx módy do Valheimu, jedno repo, každý mód ve vlastní složce s `.csproj`.

## Módy
- `LevelEasily/` jen klient (potřebuje Jotunn): položka motyky „Srovnat plochu“ srovná obdélník přichycený na mřížku
  jedním kliknutím za cenu ruční práce (motyka, krumpáč, kámen). Celý anglicky, dokumentace `LevelEasily/README.md` a `SPEC.md`. Princip zápisu terénu: `kb/teren-zapis-bez-serveru.md`.
- `RecipeBrowser/` jen klient (potřebuje Jotunn): prohlížeč receptů ve stylu JEI vedle inventáře. Vznikl, protože VNEI
  uživateli nevyhovoval: oddělená záložka místo panelu u craftingu, nejasné „výroba vs. použití“, chybí záložky a klávesa U.

## Struktura a build
- `Directory.Build.props` společné reference na hru (`ValheimDir`, výchozí Steam v `$HOME`) a cíl `Deploy`,
  který po buildu zkopíruje DLL do `BepInEx/plugins/<Mód>/`.
- Build: `dotnet build` ve složce módu. Cíl net48, reference přímo z herní složky, nic se necommituje.
- Herní API ověřuj dekompilací: `ilspycmd -t <Typ> <Managed>/assembly_valheim.dll`.

## Konvence
- Texty ve hře a popisy configu česky. Výjimka: LevelEasily je celý anglicky (kód, komentáře, texty ve hře, config,
  `LevelEasily/*.md`), přání uživatele 2026-11-20. KB zůstává česky.
- Módy, které mění svět, musí běžet i na serveru (`games/kb/valheim/server-a-klienti.md`). Preferuj čistě klientské.
