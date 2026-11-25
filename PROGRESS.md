# REAL Capture X 0.2.0 · průběh vývoje

## Funkční parita

| Funkce | Stav |
| --- | --- |
| Nativní základ .NET 10 / WinUI 3 | Sestaven a spuštěn na Windows 11 Home x64. |
| Výběr oblasti, okna a monitoru | Ověřeno po redesignu. Výřez 528 × 276 na monitoru vlevo od hlavního, samostatné okno 666 × 463 a displej 1920 × 1080. Escape obnoví náhled. |
| All-in-One | Šest přímých režimů, vlastní podnabídky mimo okraj lišty, opakování výřezu a Shift pro čtverec. Otevřeno zadání přesných rozměrů, pokročilá lupa a vícemonitorový výřez. |
| Quick Access | Kompaktní náhled s výškou podle snímku, vlastní kontextové akce a procházení historie. Ověřen náhled, obnovení, schránka, časovač a export. Exportovaný PNG je podle SHA-256 shodný s originálem. Přetažení implementováno. Zbývá ověřit cílové aplikace a pozice při smíšeném DPI. |
| Anotační editor | Win2D, šipka, tvary, kreslení, text, zvýraznění, číslování, černá redakce, ořez, krok zpět a vpřed. Ověřeny šipka, obdélník, Undo a Redo i export PNG s očekávanými změnami pixelů. Otevřeny editovatelné soubory projektu, výběr a přesun vytvořených značek a pokročilé nástroje. |
| Přizpůsobení pozadí | Vlastní barva, odsazení, zaoblení a poměr stran v pravém panelu. Ověřen vliv zapnutého pozadí na rozměry. Otevřeny vlastní obrázky pozadí, uložené presety, stín a Auto Balance. |
| Scrolling capture | Živě ověřen plovoucí ukazatel, svislé skládání a PNG 695 × 1079 v novém rozhraní. Vodorovný režim sdílí algoritmus po transpozici. Otevřeno automatické rolování a kompatibilita s lepkavým obsahem. |
| OCR | Offline angličtina a čeština. Zkopírování textu ze snímku ověřeno na syntetickém vzoru. Pokrytí jazyků a QR zatím omezené. |
| Připnutí snímku | Vizuálně ověřeno samostatné plovoucí okno. Ověřeno připnutí z nového Quick Access. Posun a volba velikosti zůstávají dostupné. Otevřena průhlednost a režim proklikávání. |
| Nahrávání | Živě ověřen záznam displeje H.264, 1920 × 1080, 30 fps, 193 snímků. Ověřena oblast 698 × 456 a kompaktní panel s živými stopkami. Volitelný mikrofon, systémový zvuk, webkamera a chytrý zoom jsou součástí nastavení před záznamem. Reálné ověření audio a video zařízení ještě chybí. |
| Studio Mode | Záznam se automaticky otevřel v přehrávači. Ověřen export přesného střihu z 6,433 s na 3,000 s v H.264, 1920 × 1080 a 30 fps. Nové rozhraní ověřeno střihovým exportem 1,380 s dle ffprobe. Hlasitost se nastavuje ve Studiu a chytrý zoom před záznamem. Otevřena postprodukční editace zoomů, kurzoru, kamery a vícestopý střih. |
| Historie | Oddělené seznamy snímků a videí s živým náhledem a otevřením v editoru nebo Studiu. Otevřeno vyhledávání a retence. |
| Zkratky a oznamovací oblast | Zkratky ověřeny včetně uložení a registrace jiné kombinace. Nová vlastní nabídka s klávesnicí, zkratkami a kotvou na oznamovací ikoně. Vizuálně ověřena stejná nabídka z lišty. |
| Nastavení | Systémový, světlý a tmavý motiv s živým náhledem, oddělené kategorie pro Quick Access a klávesy. Ověřeno uložení motivu a jeho obnovení po startu. Otevřené volby podrobněji v Obsidianu. |

## Ověření a omezení

- Debug `--self-check` ověřuje přesné kopírování pixelů, odmítnutí identického snímku, skládání obrazu, transpozici a mapování kláves.
- Self-contained Release x64 byl sestaven a spuštěn po doplnění indexu WinUI prostředků. Distribuce je v `artifacts/REAL-Capture-X-0.2.0-win-x64.zip`.
- Audit závislostí verze 0.1.0 nenašel známé zranitelné balíčky. Verze 0.2.0 používá stejné závislosti.
- Tři vizuální iterace ve skutečné aplikaci upravily zejména hustotu plovoucí nabídky, kontrast prvků obou motivů, vrstvení oken a výšku přehrávače. Obrazová dokumentace je v Obsidianu.
- Vizuální a interakční testy používají testovací profil a syntetické podklady. Pro přesné ověření kompatibility zbývají HDR, smíšené DPI, přetažení mezi aplikacemi a dlouhé záznamy s více zdroji.
- Rozhraní má vlastní styl pro Windows. Studium reference vychází z oficiálních veřejných materiálů a videí. Profil na X odpověděl HTTP 403.

## Reference

- [CleanShot X · produktové demo](https://cleanshot.com/)
- [CleanShot X · snímky](https://cleanshot.com/screenshots)
- [CleanShot X · funkce](https://cleanshot.com/features)
- [CleanShot X · changelog](https://cleanshot.com/changelog)
- [Ukázka Quick Access](https://cleanshot.com/video/home/quickaccess.mp4)

Podrobný výzkum, architektura a ověření jsou v projektové složce `Codex/Záznamy/Projekty/REAL Capture X` v existujícím Obsidianu.
