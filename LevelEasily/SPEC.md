# LevelEasily – specifikace

Položka motyky **Srovnat plochu**: jedním kliknutím srovná obdélník na mřížce metrů na cílovou výšku.
Zásada: stojí tolik, kolik by stála stejná práce ručně vanilla nástroji (motyka, krumpáč, Raise ground). Rychlejší je jen
pohodlí, ne zdroje. Vanilla chování, ze kterého model vychází: `kb/vanilla-motyka-dosah-a-cena.md`.

## Plocha a cíl
- Obdélník přichycený na celé metry (vrcholy heightmapy), šipky mění rozměry (z pohledu kamery), 1 až `Největší strana`.
- Výchozí cíl = terén pod hráčem (`ZoneSystem.GetGroundHeight`) jako vanilla Level ground, nezávisle na skoku či plavání.
- **L** zamkne cíl na horní plochu kusu stavby pod kurzorem (i když kurzor míří na jeho bok) (jinak terén pod hráčem) a mřížku; druhé L odemkne.
  **PageUp/PageDown** posune cíl o `Krok výšky` a zamkne.

## Co se s vrcholem stane
Každý vrchol dostane jeden stav (barva v náhledu):
- **rovný** (zelená): odchylka do 1 cm, nic se nedělá,
- **ukopnout** (červená) / **dosypat** (modrá): sytost podle odchylky,
- **mimo dosah** (fialová): cíl je dál než ±8 m od základní výšky; srovná se jen na hranici dosahu,
- **pod stavbou** (šedá): v okolí vrcholu (±0,45 m, od min. do max. výšky ±0,5 m) je kus stavby nebo rostlina; vynechá se,
- **chybí krumpáč** (tmavě červená): potřeboval by krumpáč a žádný použitelný v inventáři není; vynechá se,
- **nestačí zdroje** (bledá barva): nevejde se do výdrže, opotřebení nebo kamene; vynechá se.

## Cena
Pro vrchol se změnou Δ (proti dosažitelnému cíli) a dosavadní smooth deltou `s`:
- **Motyka**: každý měněný vrchol potřebuje dorovnat. Švihů Level ground = měněné vrcholy ÷ vrcholy na švih, kde vrcholy
  na švih = k², k je největší rozestup švihů, při kterém mřížka vanilla švihů srovná každý vrchol aspoň na 95 %
  (z nastavení `mud_road_v2`, vanilla vychází k = 2, tedy 4 vrcholy).
- **Krumpáč**: motyka sníží vrchol nejvýš o `1 + s`. Zbytek je objem pro krumpáč (1 m² na vrchol), počet úderů =
  objem ÷ objem jednoho úderu (z `TerrainOp` prefabu `m_spawnOnHitTerrain` krumpáče). Úder stojí výdrž jako útok
  krumpáčem (dovednost, vybavení, efekty) a opotřebení krumpáče. Použije se krumpáč z inventáře (dovednost Pickaxes),
  který ještě vykope nejvíc (zbývající údery × objem úderu); nemusí být v ruce.
- **Kámen**: motyka zvedne vrchol zadarmo nejvýš o `1 − s`, a jen když cíl není výš než terén pod hráčem (vanilla smooth
  táhne k terénu pod hráčem). Zbytek dosypání = objem pro Raise ground: švihů = objem ÷ objem švihu `raise_v2`,
  kámen = švihy × cena `raise_v2`. Každý švih Raise ground stojí výdrž a opotřebení motyky.
- **Čas**: po kliknutí je motyka zablokovaná na (švihy motyky × `m_placeDelay`) + (údery krumpáčem × `Čas úderu krumpáčem`).
- **Vykopaný materiál**: co vanilla úder do terénu vyhodí (`TerrainOp.m_spawnOnPlaced`, šance, 1–max kusů; v hloubce
  7,95 m pod základem nic, pokud prefab nepovoluje), to mód dá za započítané údery jako průměr zaokrouhlený dolů.
  Přímo do inventáře, přebytek k nohám. Nakopaný kámen se nezapočítává do kamene na dosypání téže plochy.
- **Sleva na výdrž** (config `Cena / Sleva na výdrž`, výchozí 30 %): švihy i údery stojí o tolik méně výdrže.
  Rozhodnutí uživatele po zkoušce ve hře (2026-11-20). Opotřebení, kámen ani čas sleva nemění.
- `m_noPlacementCost` (debug) ruší jen kámen, jako vanilla.

## Částečné srovnání
Když zdroje nestačí, srovná se tolik vrcholů, kolik se vejde, od nejbližších k hráči. Náhled ukáže, co se srovná, a pod
křížkem procento plochy. Nulové srovnání = hláška, co chybí.

## Povrch
- Natře se hlína (r = 1, g = b = 0) jen v buňkách, jejichž všechny čtyři rohy jsou po zápisu rovné a aspoň jeden se mění. Alfa (v Ashlands
  láva, jinde vegetace) zůstane.
- `Zachovat obdělanou a dlážděnou půdu` (výchozí zapnuto): buňky s g > 0,5 (mimo Deep North, kde g je sníh) nebo b > 0,5
  se nepřetírají.

## Kontroly před zápisem
- Vanilla stav umístění (`UpdatePlacementGhost`): hluboký sníh, dungeon, neplatné místo atd. s vanilla hláškou.
- Ward a zóna bez stavění pro **každý** vrchol, kterého se zápis dotkne (i rovné rohy natíraných buněk).
- Jiný hráč do 1,5 m od vrcholu, který se zvedne o víc než 0,5 m: odmítnout.
- Cíl pod hladinou moře: jen upozornění pod křížkem.

## Zápis (multiplayer)
1. `TerrainComp` jen těch zón, kde leží dotčené vrcholy, vytvořit/převzít (`ClaimOwnership`), počkat 0,5 s;
   motyka je mezitím zablokovaná.
2. Ověřit vlastnictví a `m_initialized`, načíst čerstvá data (`CheckLoad`, `Poke`), plán spočítat znovu jen z vrcholů
   v převzatých zónách a zopakovat všechny kontroly.
3. Zapsat `m_levelDelta = cíl − základní výška` (ne `LevelTerrain`, ten při součtu delt nad ±8 m netrefí cíl), natřít,
   `m_operations++`, `m_lastOpPoint/Radius`, `Save`, `Poke`.
4. Teprve po úspěšném zápisu strhnout výdrž, opotřebení a kámen, spustit animaci švihu a efekt.
Jakékoli selhání před krokem 3 = nic se nezapíše ani nezaplatí.

## Mimo rozsah
- Dovednost za údery krumpáčem se nepřičítá.
