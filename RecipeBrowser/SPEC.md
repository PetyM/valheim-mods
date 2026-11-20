# RecipeBrowser – specifikace 1.0

Stav: odsouhlaseno · 2026-11-20 (záložky → tagy + čipy, tagy anglicky, sbalení do lišty, implementovat fáze 1–4). Výchozí stav (0.1.0) popisuje `README.md` v kořeni repa.
Fakta o herních datech: `kb/lokalizace-chybejici-klic.md`, `kb/filtr-nedostupnych-predmetu.md`, `kb/zdroje-biomu-predmetu.md`.

## 1. Katalog: čistota a metadata

### 1.1 Filtr nedostupných položek
Pořadí pravidel. Výsledek každé položky se loguje (debug), aby šlo dohledat, proč něco chybí.
1. Pevná vyřazení: `ItemType` Customization a Attach_Atgeir, prefaby `SP_*` a `FW_*`, jména `*_attach`, `*_nochest`,
   `*_ragdoll`, `*Cheat*`, `*Test*`.
2. DLC, které není nainstalované (`m_dlc` + `DLCMan.IsDLCInstalled`).
3. Chybějící překlad: vanilla položku skrýt. Mód položku ukázat pod očištěným jménem prefabu (`SwordBlackmetal_TW`
   → „Sword Blackmetal TW“).
4. Duplicity se stejným `m_name`: nechat tu, která má recept nebo zdroj.
5. Bez zdroje i bez použití (žádný recept, konverze, drop, sběr, obchodník, ingredience): skrýt.
   Config `Katalog / Skrýt nedostupné` (výchozí zapnuto).
6. Stavby: skrýt `m_repairPiece`/`m_removePiece`, stavby bez surovin (kromě semínek a sazenic) a stanice, které nejsou
   v žádné tabulce nástroje (`fire_pit_haldor`, `goblin_bed`…). Hostiny (`Feast`) brát jako jídlo, ne jako nástroj.
7. Uživatelský ignore list v configu (přesná jména, `*suffix`, `prefix*`).

### 1.2 Metadata položky
`Entry` dostane: hlavní kategorii, podkategorie, množinu tagů, biom (pořadí postupu), úroveň stanice, sadu
(`m_setName`) a hodnoty jídla. Tagy se počítají jednou při stavbě katalogu.

### 1.3 Progression (biom)
- Pořadí: Louky 0, Černý les 1, Bažina 2, Hory 3, Pláně 4, Mlžné země 5, Popelavé země 6, Hluboký sever 7.
  Moře má vlastní tag `#ocean` a pořadí mezi Bažinou a Horami. Bez zdroje = „neznámý“, řadí se na konec.
- Seed z přímých zdrojů (nejnižší biom): vegetace a její dropy, tvorové ze spawn listů a jejich dropy. Biom
  obchodníka a lokace se bere jen jako max(biom místa, biom bosse z `m_requiredGlobalKey`).
- Propagace do ustáleného stavu. Recept má max(biomy ingrediencí, biom stanice a její úrovně). Položka má min přes
  všechny cesty, jak ji získat. U „stačí jedna ingredience“ se bere min. Vylepšení (q>1) se do propagace nepočítají.
- Biom stanice je biom její stavby, úroveň L je L-tý nejnižší biom jejích rozšíření.
- Malá vanilla override tabulka (Chitin, Serpent meat, Coins, Amber, Ruby, dropy bossů) a uživatelský override v configu.
- Biomový index se staví líně po vstupu do světa. `ZoneSystem` v menu neexistuje a spawn listy módů můžou dorazit
  až se synchronizací ze serveru.

### 1.4 Nový pohled „Jak získat“
Vedle receptů karty bez výroby: drop z tvora (se šancí), sběr, těžba (s potřebným tierem krumpáče), strom, truhla
v lokaci, obchodník (cena, podmínka). Na kartě je biom. Data padnou zadarmo ze seed fáze 1.3.

## 2. Hledání a tagy (nahradí záložky kategorií)
- Slova: všechna musí sedět v názvu nebo prefabu, jako dnes.
- `@mód`: jako dnes.
- `#tag`: tag položky. Dvojtečka znamená „a zároveň“, takže `#weapons:plains` = `#weapons` + `#plains`
  a `#food:stamina` = `#food` + `#stamina`. Prefix stačí (`#swo` → `#swords`), pokud je jednoznačný.
- `-x`: negace libovolného tokenu (`-@Therzie`, `-#pieces`, `-kůže`).
- `^název`: položky, které mají danou položku jako ingredienci.
- `|` mezi tokeny: nebo (`#swords|#axes`).
- Našeptávač pod polem: při psaní `#`/`@` nabídne tagy nebo módy s počtem výsledků, Tab doplní.
- Pod polem řádek „čipů“ (Zbraně, Zbroj, Jídlo, Materiály, Stavby…). Klik vloží nebo odebere tag v hledání,
  pravý klik ho vloží negovaný. Čipy jsou jen zkratka, stav je vždy vidět v textu hledání.

### 2.1 Tagy (anglicky, krátké; v našeptávači s českým popisem)
- Kategorie: `#materials #food #potions #weapons #armor #ammo #tools #pieces #trophies #fish #misc`.
- Zbraně: `#swords #axes #battleaxes #clubs #sledges #spears #polearms #knives #fists #bows #crossbows #staffs
  #shields` a `#1h`/`#2h`. Štíty: `#tower #buckler`.
- Zbroj: `#helmet #chest #legs #cape #gloves #utility #trinket`, sady `#set:<jméno>`.
- Jídlo: `#health #stamina #eitr #balanced` (převažující složka), `#meads`, `#drinks`.
- Stavby podle `PieceCategory`: `#crafting #building #stone #furniture #feasts`, a `#seeds`.
- Biomy: `#meadows #blackforest #swamp #mountains #plains #mistlands #ashlands #deepnorth #ocean #unknown`.
- Stav: `#craftable` (teď jde vyrobit z inventáře a u blízké stanice), `#bookmarked`, `#station:<stanice>`.

## 3. Řazení
- Výchozí: biom → kategorie → podkategorie → úroveň stanice → název. Nad mřížkou přepínač Postup / Název / Mód.
- Volitelně oddělovač biomu v mřížce (tenký řádek s názvem biomu). Config, výchozí vypnuto.

## 4. UI
- **Schovat:** klávesa (config, výchozí `Y`, platí jen při otevřeném inventáři) a tlačítko v hlavičce panelu sbalí oba panely do malé lišty s nadpisem.
  Stav se pamatuje. Okno receptu se dá otevřít i se sbaleným panelem (R/U nad inventářem).
- **Přesun a resize:** každý panel (výsledky, záložky, okno receptu) jde přetáhnout za hlavičku a zvětšit za roh.
  Velikost mřížky se přichytává na celá políčka (sloupce × řádky), okno receptu na celé karty.
  Pozice a velikosti se ukládají do configu relativně k obrazovce, takže přežijí změnu rozlišení.
  Tlačítko nebo config „Obnovit rozložení“.
- **Listování:** kolečko listuje po řádcích, ne po stránkách. Tlačítka `<` `>` listují po stránkách.
- **Okno receptu:** skupiny stanic se zalomí bez omezení řádků (dnes se zbytek ztratí). Poznámky na kartě se zalamují.
  Karta ukáže biom receptu.
- **Vyrobit:** klik na kartu, když stojíš u správné stanice, vybere recept ve vanilla seznamu craftingu.
- **Počty „máš“:** volitelně i z truhel v okolí, pokud běží CraftFromContainers (soft závislost, jinak jen inventář).
- **Záložky:** přetažením měnit pořadí. Skupiny záložek až v další verzi.

## 5. Výkon a robustnost
- Katalog se přestaví při změně počtu receptů i předmětů v ObjectDB. Biomový index se přepočítá jen tehdy.
- Hledání nad předpočítanými tagy (HashSet), bez LINQ alokací na každý znak. Mřížka recykluje sloty (už dnes).
- Stavba katalogu se měří a loguje (cíl < 200 ms, biomový index může běžet v coroutině).

## 6. Mimo rozsah 1.0
Server/sync, úprava receptů, cheaty (spawn předmětu), skupiny záložek, export dat.

## 7. Fáze implementace
1. Katalog: filtr 1.1, metadata a tagy 1.2, nové hledání 2 a čipy místo záložek, řazení podle kategorie.
2. Progression: biomový index 1.3, řazení 3, pohled „Jak získat“ 1.4.
3. UI: schování, přesun a resize, uložení rozložení, listování po řádcích, opravy okna receptu.
4. Doplňky: vyrobit z karty, počty z truhel, `#craftable`, pořadí záložek.
