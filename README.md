# Valheim módy

## LevelEasily
Položka motyky „Level area“: srovná obdélník jedním kliknutím za cenu ruční práce. Mód je celý anglicky,
dokumentace v [LevelEasily/README.md](LevelEasily/README.md) a specifikace v [LevelEasily/SPEC.md](LevelEasily/SPEC.md).

## RecipeBrowser
Prohlížeč receptů ve stylu JEI (potřebuje Jotunn, jen klient). Při otevřeném inventáři jsou dole dva panely: výsledky
s hledáním vlevo, záložky vpravo. Políčka jsou stejně velká jako v inventáři. Nad panelem se otevírá okno receptu.

**Hledání** (tokeny oddělené mezerou musí platit všechny):
- `slovo` v názvu nebo prefabu, `@mód` podle módu (`@Therzie`, vanilla je `@Valheim`).
- `#tag` podle tagu, dvojtečka znamená „a zároveň“ (`#weapons:plains`, `#food:stamina`). Stačí jednoznačný začátek (`#swo`).
- `-` před čímkoli vyřadí (`-@Therzie`, `-#pieces`, `-kůže`), `^název` najde, k čemu je předmět ingrediencí (`^Wood`),
  `|` znamená nebo (`#swords|#axes`).
- Při psaní `#`, `@` a `^` se pod polem nabízí doplnění s českým popisem a počtem výsledků, **Tab** vezme první.
- Pod polem jsou čipy (Zbraně, Zbroj, Jídlo, Lektvary, Materiály, Munice, Nástroje, Stavby, Trofeje, Ryby, Ostatní,
  Vyrobitelné, Záložky). Kategorie se chovají jako záložky: klik nahradí předchozí kategorii, Ctrl+klik ji přidá jako
  „nebo“ (`#weapons|#armor`). Vyrobitelné a Záložky se přidávají k filtru. Pravý klik tag vyřadí (`-#tag`).

**Tagy:**
- kategorie `#materials #food #potions #weapons #armor #ammo #tools #pieces #trophies #fish #misc`;
- zbraně `#swords #axes #battleaxes #clubs #sledges #spears #polearms #knives #fists #bows #crossbows #staffs #pickaxes
  #shields #tower #buckler #1h #2h`;
- zbroj `#helmet #chest #legs #cape #gloves #utility #trinket`, sady `#set:<jméno>`;
- jídlo `#health #stamina #eitr #balanced #meads #drinks #feasts`, stavby `#crafting #building #stone #furniture #seeds`;
- biomy `#meadows #blackforest #swamp #ocean #mountains #plains #mistlands #ashlands #deepnorth #unknown`;
- stav `#craftable` (jde vyrobit teď z toho, co máš, u stanice v dosahu), `#bookmarked`, `#station:<stanice>`.

**Řazení** tlačítkem vedle hledání: postup (biom → kategorie → podkategorie → úroveň stanice → název), název, mód.
Biom se počítá z vegetace, spawnů, dropů a receptů (nejlevnější cesta). Co nejde odvodit, je „Neznámý“ a řadí se na konec.

**Klávesy** nad předmětem kdekoli (panel, inventář, truhla, seznam craftingu): **R** jak vyrobit, **U** k čemu je,
**A** záložka (v záložkách ji odebere). Dokud kurzor stojí nad předmětem, nehýbou postavou. V panelu a v receptech
levé tlačítko = jak vyrobit, pravé = k čemu je. **Backspace** zpět, **Esc** zavře nejdřív okno receptu.
**Y** sbalí panely do malé lišty a zase je rozbalí (jen při otevřeném inventáři, stejně jako tlačítko „–“ v hlavičce).
R a U fungují i se sbalenými panely.

**Okno receptu:** záložky „Jak vyrobit“, „Jak získat“ (drop z tvora se šancí, sběr, těžba s úrovní krumpáče, kácení,
truhla, obchodník s cenou a podmínkou, rybaření), „Vylepšení“ a „K čemu je“. Prázdné se skryjí. Pod nimi jsou skupiny
podle stanice nebo akce, zalamují se na víc řádků. Karta ukáže stanici a úroveň, biom, ingredience s počty (zeleně máš
dost, červeně chybí, v závorce kolik máš) a poznámky. Když stojíš u správné stanice, klik na kartu vybere recept
ve vanilla seznamu výroby nebo vylepšení.

**Rozložení:** panely i okno receptu jdou přetáhnout za hlavičku a zvětšit za oranžový roh. Mřížka se přichytí
na celá políčka, okno na celé karty. Kolečko listuje po řádcích (po kartách), `<` `>` po stránkách. Pozice se ukládají
jako podíl obrazovky, takže přežijí změnu rozlišení. Tlačítko „Obnovit rozložení“ v hlavičce záložek je vrátí do pruhu dole.
Záložky jde přetažením přeřadit.

**Nastavení** `BepInEx/config/michal.recipebrowser.cfg` (i v Configuration Manageru):
- `Klávesy`: Jak vyrobit, K čemu je, Záložka, Zpět, Schovat.
- `Zobrazení`: Řádky (výška výchozího pruhu), Řazení, Sbaleno, Oddělovač biomu (při řazení podle postupu řádek
  s názvem biomu před každou skupinou, výchozí vypnuto). `Rozložení`: uložené pozice a Obnovit rozložení.
- `Katalog`: Skrýt nedostupné (předměty bez zdroje i použití, výchozí zapnuto), Ignorovat (prefaby: přesné jméno,
  `*přípona`, `předpona*`), Logovat filtr (do logu každá skrytá položka s důvodem).
- `Biomy / Přepsat biom`: ruční biom, např. `Chitin=ocean, MyModOre=plains`.
- `Počty`: Truhly v okolí (Auto = jen když běží CraftFromContainers nebo podobný mód, Always, Never) a Dosah truhel.
- Záložky jsou v `BepInEx/config/michal.recipebrowser.bookmarks.txt`.

Katalog se postaví při prvním otevření inventáře ve světě a znovu, když se změní počet receptů nebo předmětů
(synchronizace ze serveru), spawn listy (i módů) nebo nastavení katalogu. Lokace a místnosti dungeonů se po vstupu
do světa načítají na pozadí (asset bundly po dávkách); po dokončení se katalog jednou přestaví a truhly, sběr,
obchodníci a tvorové z lokací dostanou biom lokace. Do té doby mají truhly biom jen odhadnutý podle jména prefabu.
Souhrn (počty, čas, skryté podle důvodu, lokace) je v logu pod `RecipeBrowser.Data`.

## Build
```
cd LevelEasily && dotnet build      # nebo RecipeBrowser
```
Jiná herní složka: `dotnet build -p:ValheimDir=/cesta/k/Valheim`.

## Vazby mimo repo
- Build čte DLL ze Steam složky Valheimu (`~/.local/share/Steam/steamapps/common/Valheim`) a zapisuje do jejích
  `BepInEx/plugins/<Mód>/`.
- `dotnet-sdk` z pacmanu, `ilspycmd` jako globální dotnet tool (`~/.dotnet/tools`).
