# REAL Capture X 0.1.0 · průběh vývoje

## Funkční parita

| Funkce | Stav |
| --- | --- |
| Nativní základ .NET 10 / WinUI 3 | Sestaven a spuštěn na Windows 11 Home x64. |
| Výběr oblasti, okna a monitoru | Ověřeno živě. Test zahrnuje monitor 1920 × 1080, tažený výřez a okno 666 × 463. |
| All-in-One | Tři hlavní screenshot režimy, nabídka dalších akcí, opakování výřezu a Shift pro čtverec. Otevřeno zadání přesných rozměrů, pokročilá lupa a vícemonitorový výřez. |
| Quick Access | Ověřen náhled, výběr staršího snímku, obnovení, schránka, časovač a export. Exportovaný PNG je podle SHA-256 shodný s originálem. Přetažení implementováno. Zbývá ověřit cílové aplikace a pozice při smíšeném DPI. |
| Anotační editor | Win2D, šipka, tvary, kreslení, text, zvýraznění, číslování, černá redakce, ořez, krok zpět a vpřed. Živě ověřena šipka. Otevřeny editovatelné soubory projektu, výběr a přesun vytvořených značek a pokročilé nástroje. |
| Přizpůsobení pozadí | Vlastní barva, odsazení, zaoblení a poměr stran. Otevřeny vlastní obrázky pozadí, uložené presety, stín a Auto Balance. |
| Scrolling capture | Živě ověřeno svislé skládání a PNG 695 × 1404 na syntetické ploše. Vodorovný režim sdílí algoritmus po transpozici. Otevřeno automatické rolování a kompatibilita s lepkavým obsahem. |
| OCR | Offline angličtina a čeština. Zkopírování textu ze snímku ověřeno na syntetickém vzoru. Pokrytí jazyků a QR zatím omezené. |
| Připnutí snímku | Vizuálně ověřeno samostatné plovoucí okno. Implementován posun a volba velikosti. Otevřena průhlednost a režim proklikávání. |
| Nahrávání | Živě ověřen záznam displeje H.264, 1920 × 1080, 30 fps, 193 snímků. Implementovány oblast, volitelný mikrofon, systémový zvuk a kamera. Reálné ověření audio a video zařízení ještě chybí. |
| Studio Mode | Záznam se automaticky otevřel v přehrávači. Ověřen export přesného střihu z 6,433 s na 3,000 s v H.264, 1920 × 1080 a 30 fps. Implementována hlasitost a chytrý zoom při záznamu. Otevřena postprodukční editace zoomů, kurzoru, kamery a vícestopý střih. |
| Historie | Místní snímky i video, nativní seznam a otevření v editoru nebo Studiu. Otevřeno vyhledávání a retence. |
| Zkratky a oznamovací oblast | Zkratky ověřeny včetně uložení a registrace jiné kombinace. Nativní kontextové menu a registrace ikony při restartu Exploreru. |
| Nastavení | Pozice, časovač, viditelnost náhledu, automatické kopírování, kurzor a přemapování kláves. Otevřené volby podrobněji v Obsidianu. |

## Ověření a omezení

- Debug `--self-check` ověřuje přesné kopírování pixelů, odmítnutí identického snímku, skládání obrazu, transpozici a mapování kláves.
- Self-contained Release x64 byl sestaven a spuštěn. Distribuce je v `artifacts/REAL-Capture-X-0.1.0-win-x64.zip`.
- NuGet audit s transitivními závislostmi nevrátil známé zranitelné balíčky z aktuálních zdrojů.
- Vizuální a interakční testy používají testovací profil a syntetické podklady. Pro přesné ověření kompatibility zbývají HDR, smíšené DPI, přetažení mezi aplikacemi a dlouhé záznamy s více zdroji.
- Rozhraní má vlastní styl pro Windows. Studium reference vychází z oficiálních veřejných materiálů a videí. Profil na X odpověděl HTTP 403.

## Reference

- [CleanShot X · snímky](https://cleanshot.com/screenshots)
- [CleanShot X · funkce](https://cleanshot.com/features)
- [CleanShot X · changelog](https://cleanshot.com/changelog)
- [Ukázka Quick Access](https://cleanshot.com/video/home/quickaccess.mp4)

Podrobný výzkum, architektura a ověření jsou v projektové složce `Codex/Záznamy/Projekty/REAL Capture X` v existujícím Obsidianu.
