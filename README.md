# REAL Capture X

Nativní nástroj pro snímání Windows 11 x64. Rozhraní používá WinUI 3. Snímky pořizuje Windows Graphics Capture přes Direct3D, PNG kóduje Windows Imaging a záznamy MP4 kóduje Windows Media Transcoder.

## Spuštění

Přenosnou aplikaci spusťte souborem `artifacts/RealCaptureX/RealCaptureX.exe`. Soubory ve složce sestavení ponechte pohromadě. Pro sestavení ze zdrojového kódu je potřeba .NET 10 SDK.

```powershell
dotnet run --project RealCaptureX.csproj
```

| Zkratka | Akce |
| --- | --- |
| Ctrl+Shift+1 | Výběr oblasti na monitoru pod kurzorem |
| Ctrl+Shift+2 | Výběr a snímek okna |
| Ctrl+Shift+3 | Snímek monitoru pod kurzorem |
| Ctrl+Shift+4 | Zobrazení panelu snímání |
| Ctrl+Shift+5 | Obnovení Quick Access |
| Shift při tažení | Čtvercový výběr |
| Esc | Zrušení výběru |

- Quick Access nabízí anotaci, schránku, export PNG, přetažení souboru, připnutí a rozpoznání textu.
- Nabídka `…` v panelu snímání nabízí opakování posledního výřezu, OCR, svislé a vodorovné rolování a záznam oblasti nebo displeje.
- Záznam ukládá MP4 v H.264. Systémový zvuk, mikrofon, kamera a chytrý zoom se zapínají před nahráváním. Studio umí přehrávání, nastavení začátku a konce, hlasitost a export.
- Ikona v oznamovací oblasti otevírá panel nebo kontextovou nabídku. Klávesy, pozici náhledu, časovač a chování schránky lze upravit v nastavení.

Lokální historie snímků, nahrávky a nastavení jsou v `%LOCALAPPDATA%\REAL Capture X`. OCR s angličtinou a češtinou běží místně přes Tesseract. Závislosti mají přehled v [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Sestavení přenosné verze pro Windows 11 x64

```powershell
dotnet publish RealCaptureX.csproj -c Release -r win-x64 --self-contained -o artifacts/RealCaptureX
```

Spusťte `RealCaptureX.exe` ze složky se soubory sestavení. Stav implementace a ověření je v [PROGRESS.md](PROGRESS.md).

## Debug ověření

Spuštění Debug EXE s `--self-check` prověří ořez, skládání, transpozici a virtuální klávesy. Hodnota `REAL_CAPTURE_X_INSPECT=1` zpřístupní plovoucí okna nástrojům QA a použije dočasný profil `%TEMP%\REAL Capture X QA`.

## Přispěvatelé

Na vývoji první verze se podílel [OpenAI Codex](https://github.com/codex).
