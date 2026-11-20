# Valheim módy

## LevelEasily
Nová položka v menu motyky **Srovnat plochu**. Náhled ukáže obdélník přichycený na mřížku metrů
(vrcholy heightmapy) s body obarvenými podle toho, co se s nimi stane:
zelená = rovné, červená = ukopne se, modrá = dosype se, fialová = mimo dosah ±8 m od původního terénu,
šedá = pod stavbou nebo rostlinou (vynechá se), tmavě červená = chybí krumpáč, ztmavené = nestačí zdroje.
Klik srovná plochu najednou a natře ji hlínou (jako vanilla Level ground, tráva na ní neroste). Pod křížkem je cena a nápověda.

- **Šipky** mění velikost (vlevo/vpravo = šířka z pohledu kamery, nahoru/dolů = délka), výchozí 4 × 4 m, nejvíc 16 m.
- Cíl je terén pod hráčem (jako vanilla). **L** zamkne cíl na horní plochu kusu stavby pod kurzorem a zároveň mřížku,
  takže další plochy navazují přesně vedle sebe. **PageUp/PageDown** posune cíl o 0,1 m (a zamkne).
- Cena jako ruční práce (podrobně `LevelEasily/SPEC.md`): švihy motyky (asi 1 na 4 body), odkopání hlouběji než 1 m
  údery krumpáčem z inventáře (výdrž a jeho opotřebení), dosypání nad 1 m nebo nad terén pod hráčem kámen podle Raise ground.
  Motyka je pak zablokovaná na dobu, kterou by práce trvala. Na co zdroje nestačí, to se nesrovná (od nejbližších bodů).
  Výdrž má slevu 30 % proti ruční práci (`Cena / Sleva na výdrž`).
- Obdělaná a dlážděná půda se srovná, ale nepřetře (volba v configu). V Ashlands nevznikne láva.
- Respektuje wardy a zóny bez stavění v celé ploše a vanilla omezení motyky. Ostatní hráči ani server mód mít nemusí.
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
