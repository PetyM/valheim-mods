# Valheim módy

## LevelEasily
Nová položka v menu motyky **Srovnat plochu**. Náhled ukáže obdélník přichycený na mřížku metrů
(vrcholy heightmapy) s body obarvenými podle odchylky od cílové výšky:
zelená = rovné, červená = ukopne se, modrá = dosype se. Klik srovná celou plochu najednou a natře ji hlínou
(jako vanilla Level ground, tráva na ní neroste). Pod křížkem je cena a nápověda kláves.

- **Šipky** mění velikost (vlevo/vpravo = šířka z pohledu kamery, nahoru/dolů = délka), výchozí 4 × 4 m, nejvíc 16 m.
- Cíl je výška nohou. **L** zamkne cíl na horní plochu kusu stavby pod kurzorem (jinak nohy) a zároveň mřížku,
  takže další plochy navazují přesně vedle sebe. **PageUp/PageDown** posune cíl o 0,1 m (a zamkne).
- Cena: počet švihů = změněné vrcholy ÷ vrcholy jednoho vanilla Level ground; za každý švih výdrž a opotřebení
  jako při vanilla použití motyky. Dosypání stojí kámen podle poměru vanilla Raise ground. Pod křížkem je vidět předem.
- Respektuje wardy a zóny bez stavění. Ostatní hráči ani server mód mít nemusí.
- Nastavení: `BepInEx/config/michal.leveleasily.cfg` (i v Configuration Manageru).

## RecipeBrowser
Dva panely ve stylu JEI při otevřeném inventáři v pruhu dole (výsledky vlevo, záložky vpravo), políčka stejně velká jako
v inventáři, počet řádků v configu (`Zobrazení / Řádky`, výchozí 5). Okno receptu je nad panelem výsledků.
Nad mřížkou výsledků jsou záložky kategorií: Materiály, Jídlo, Zbraně, Zbroj, Munice, Nástroje, Stavby, Trofeje, Ostatní.
- Hledání: slova v názvu (všechna musí sedět), `@text` filtruje mód, hledá v názvu pluginu i ve složce (`@Therzie`, `@Armory`).
  Vanilla je `@Valheim`. Nad políčkem je herní tooltip (u předmětů stejný jako v inventáři, u staveb popis) a pod ním
  druh (předmět/stavba), interní jméno prefabu a mód.
- Nad předmětem kdekoli (panel, inventář, truhla, seznam craftingu): **R** jak vyrobit, **U** k čemu je, **A** záložka
  (v záložkách A odebere). Dokud kurzor stojí nad předmětem, tyhle klávesy nehýbou postavou. V panelu a v receptech
  levé tlačítko = jak vyrobit, pravé = k čemu je. **Backspace** zpět, **Esc** zavře jen okno receptu.
- Okno receptu: záložky „Jak vyrobit“, „Vylepšení“ (vylepšení předmětu i vylepšení, kde je ingrediencí) a „K čemu je“.
  Prázdné záložky se skryjí; když žádaná záložka (R/U) nic nemá, otevře se první neprázdná.
  Pod nimi záložky podle stanice nebo akce (stanice, Ruční výroba, Stavba · nástroj, zpracování v peci…).
  Karta: druh, stanice a úroveň, ingredience s počty (zeleně máš dost, červeně chybí, v závorce kolik máš v inventáři),
  výsledek, poznámky (palivo, čas, nástroj, „stačí jedna z ingrediencí“). Truhly v okolí se nepočítají.
- Zdroje: recepty z ObjectDB včetně vylepšení, stavby ze všech nástrojů (jen povolené), pece, kvasné sudy, varné stanice.
  Katalog se načte při otevření inventáře a znovu, když se změní počet receptů (synchronizace ze serveru).
- Záložky: `BepInEx/config/michal.recipebrowser.bookmarks.txt`.

## Build
```
cd LevelEasily && dotnet build      # nebo RecipeBrowser
```
Jiná herní složka: `dotnet build -p:ValheimDir=/cesta/k/Valheim`.

## Vazby mimo repo
- Build čte DLL ze Steam složky Valheimu (`~/.local/share/Steam/steamapps/common/Valheim`) a zapisuje do jejích
  `BepInEx/plugins/<Mód>/`.
- `dotnet-sdk` z pacmanu, `ilspycmd` jako globální dotnet tool (`~/.dotnet/tools`).
