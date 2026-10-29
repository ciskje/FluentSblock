# FluentSblock — TODO.md

Tracciamento attività. Spuntare solo ciò che è verificato con build verde.
Versione corrente: `0.2.0` (vedi `VERSION`).

## Legenda versioni

- `X` MAJOR: breaking / cambio stack (es. WPF→WinUI3).
- `Y` MINOR: feature visibile (eject, nuove colonne, nuova lingua).
- `Z` PATCH: fix, testi, stile.

## 0.1.0 — base funzionante (in corso)

Obiettivo: elenca blocchi per disco rimovibile + campo file, menu EFIGS, stile Fluent.

- [x] Spec `AGENT.md` approvata
- [x] Scaffold `src/FluentSblock` (csproj net8.0-windows, App, MainWindow, Models, Services, Themes, Resources EFIGS)
- [x] `DriveEnumerator`: lettere rimovibili + label/FS/spazio
- [x] `HandleEnumerator`: NtQuery + DuplicateHandle + NT→DOS, filtro per volume, async + cancel + timeout
- [x] `FileLockChecker`: Restart Manager per singolo path
- [x] UI: Menu File/View/Help, ComboBox drive, DataGrid holders, card Controlla-file, StatusBar (stato + admin + versione)
- [x] Stile Fluent: corner 4/8, Segoe UI Variable, Mica/DWM fallback, light/dark/system, high-contrast
- [x] Resx EFIGS completi (en master, fr/it/de/es stesse chiavi), switch runtime + `--lang=`
- [x] `EjectService` stub disabilitato con tooltip "in 0.2.0"
- [x] Build verde con `--artifacts-path C:/Temp/opencode/FluentSblock-artifacts`
- [ ] Test manuali: (a) no-USB empty, (b) USB+Notepad holder, (c) campo file locked, (d) 5 lingue
- [ ] Tag `v0.1.0`

## 0.1.1 — fix rilevamento USB esterni (verificato)

Obiettivo: E: (SSD USB, `DRIVE_FIXED` + BusType USB) deve comparire.

- [x] `DriveEnumerator`: include `DRIVE_FIXED` se `IsUsbOrHotplug` (BusType USB via `IOCTL_STORAGE_QUERY_PROPERTY` o `DeviceHotplug` via `IOCTL_STORAGE_GET_HOTPLUG_INFO`)
- [x] Test harness `C:/Temp/opencode/TestDrives`: `count=1`, `E: | COR500 | exFAT`, `C/D=False E=True`
- [x] `VersionText` dinamica da assembly, `AboutText` 0.1.1 nei 5 resx, `VERSION`+csproj `0.1.1`, rebuild verde
- [ ] Test manuale: aprire app da `run-debug.bat` e verificare E: visibile + scansione holder
- [ ] Tag `v0.1.1`

## 0.1.5 — card file spostabile sopra/sotto (da verificare: build + manuale)

Obiettivo: la card "Controlla file" si sposta sopra/sotto i risultati con pulsante in intestazione.

- [x] Splitter trascinabile tra risultati e card file (al posto del pulsante sposta); card con altezza minima
- [x] Stesso file aperto più volte: una sola riga (primo handle)
- [x] Sfoglia: stessa finestra per file e cartelle (`BrowseTitle` 5 lingue)
- [x] Icone vettoriali inline (refresh/eject/exit/theme/language/about/folder/search/chevron) su bottoni, menu, lista drive; barra accento DWM; pallino stato; conteggi in accento
- [x] Spinner: icona refresh e lente ruotano via timer UI durante la scansione, stop + reset a zero in `finally`
- [x] `AboutText` 0.1.5; `VERSION`+csproj `0.1.5`
- [x] Build verde + smoke GUI (avvio, resta viva, icone/risorse ok)
- [x] `dist/`: release self-contained single-file (154 MB) + `VERSION.txt` + `LEGGIMI.txt`, smoke su SSD ok
- [x] `installer/`: setup per-utente Inno (`FluentSblock.iss` + `build-setup.bat`, GUID dedicato, no UAC, no check .NET) → `FluentSblock-Setup.exe` 49 MB in `dist/`; verifica statica ok (firma versione corretta); silent-test bloccato da SmartScreen (exe non firmato) — prima esecuzione: "Ulteriori informazioni" → "Esegui comunque"
- [ ] Test manuale: click sposta sopra/sotto, testi in 2 lingue
- [ ] Tag `v0.1.5`

## 0.1.4 — fix path di rete UNC/MUP (verificato headless)

Obiettivo: il campo file deve trovare i lock anche su drive mappati (`Z:`) e UNC.

- [x] Match multiforma: DOS + NT (`QueryDosDevice`) + UNC (`WNetGetUniversalName`) + MUP per campo file e scansione drive
- [x] Nomi MUP mostrati in forma UNC (`\\server\share\...`) invece di `\Device\Mup\...`
- [x] `AboutText` 0.1.4; `VERSION`+csproj `0.1.4`; build verde
- [x] Harness headless su `Z:\Lavorazioni\2026\AI\FluentSblock`: COUNT=14 (explorer, opencode, dotnet, cmd, bash) con utente
- [ ] Test manuale: campo file con path `Z:` e UNC; drive `Z:` (se rimovibile/mappato)
- [ ] Tag `v0.1.4`

## 0.1.3 — fix da audit repo (da verificare manuale)

Obiettivo: chiudere i finding Must/Should dell'audit 2026-10-28 senza nuove feature.

- [x] F5 collegato a refresh (era binding vuoto); singolo scan all'avvio (niente più doppio scan)
- [x] `IsUsbOrHotplug` apre `\\.\X:` con accesso 0 (funziona anche non-admin)
- [x] Nome processo dall'handle già aperto (`QueryFullProcessImageNameW`), fallback `pid:N`
- [x] Restart Manager sotto token (30 s valgono anche per RM); ramo if/else identico rimosso
- [x] Tema Light/Dark disabilitati (0.2.0); `ApplySystemBackdrop` vuoto + `EjectService.IsAvailable` rimossi; `EjectNotImplemented` in resx
- [x] `RemovableDrive` ridotto a Letter+Label (via FS/spazio/DevicePath morti e P/Invoke doppi); lista mostra label
- [x] `LocksFound`/`RmFallback` in resx (5 lingue); `VersionText` senza letterali datati; `_cts` disposed
- [x] `AboutText` 0.1.3; `VERSION`+csproj `0.1.3`; build verde
- [x] Harness headless su C: (vedi 0.1.2, da ripetere dopo i fix)
- [ ] Test manuali: (a) no-USB empty, (b) USB+Notepad holder+utente, (c) campo file, (d) 5 lingue, F5, non-admin E: visibile
- [ ] Tag `v0.1.3`

Obiettivo: scansione più veloce/robusta seguendo il flusso reale di handle64.exe
(`FUN_140007a40` probe classe 64, `FUN_140006190` open-per-PID, `SetThreadErrorMode`).

- [x] Probe-first: `NtQuerySystemInformation(64, NULL, 0)` per dimensione esatta; errore esplicito localizzato (`ScanFailedHandles`) invece di lista vuota silenziosa
- [x] Un `OpenProcess` per PID (raggruppa handle, ~200 open invece di ~120k) + `SetThreadErrorMode(SEM_FAILCRITICALERRORS)` a inizio scansione
- [x] Mappa DOS longest-prefix-first (fix collisione `HarddiskVolume1` vs `HarddiskVolume10`)
- [x] `NtQueryObject` nome con buffer auto-espandente + timeout 100 ms su thread dedicato (no hang su pipe; dup sempre chiuso dal worker)
- [x] Colonna Utente reale via token→SID→`LookupAccountSidW` (riusa handle processo già aperto)
- [x] Nuove chiavi resx `ScanProgress`/`ScanFailedHandles` nelle 5 lingue; `AboutText` 0.1.2; `VERSION`+csproj `0.1.2`; build verde
- [x] Harness headless su C:: 160316 handle scansionati, 3022 holder File, `WITHUSER=3022`, nessun hang
- [ ] Test manuali: (b) USB+Notepad holder con utente visibile, (c) campo file locked
- [ ] Tag `v0.1.2`

## 0.2.0 — espulsione (in corso: eject implementato, resto invariato)

- [x] `EjectService`: `CM_Request_Device_EjectW` bottom-up (disco→parent→grandparent) con skip non-rimovibili, veto localizzato, conferma se holder>0, pulsanti abilitati con drive selezionato, refresh lista dopo eject
- [x] Colonna Accesso (mask `GrantedAccess`, già raccolta) + ordinamento DataGrid nativo; resx `ColAccess`
- [x] Export CSV/TSV (menu File, come `handle -v/-vt`); resx `MenuExportCsv/Tsv`
- [x] Refresh auto su `WM_DEVICECHANGE` (hook HwndSource, guardia anti-reentranza)
- [x] USB-HDD: `HasRemovableAncestor` (DN_REMOVABLE nella catena) come terzo check in `DriveEnumerator`
- [x] `QueryNameTimeoutMs` configurabile via proprietà (20..5000, default 100); UI in 0.3.0 con persistenza
- [x] Log `%TEMP%/FluentSblock.log` + Help>Mostra log; resx `MenuShowLog`
- [x] Harness: Access popolati (`0x0012019F`...), catena C: ok, `HasRemovableAncestor(C:)=False`, log scritto; smoke GUI ok
- [x] `PreviewEjectTarget` per diagnostica senza espellere; errori Win32 catturati subito (niente più 0x0 mascherati)
- [x] Anello blu rotante nel pulsante check-file durante la scansione (lente ferma a riposo)
- [x] Catena eject validata headless su C: (NVMe→controller→root); fix offset stringa detail SetupDi (era +8, è +4)
- [x] `EjectOk/Veto/Error/ConfirmTitle/ConfirmText` in resx (5 lingue); `AboutText` 0.2.0; `VERSION`+csproj `0.2.0`; build verde
- [ ] Test manuale: collegare E:, verificare preview implicita, espellere davvero (conferma con holder, veto con file aperto)
- [ ] Test manuale: export CSV/TSV, Help>log, inserimento USB → refresh auto
- [ ] Tag `v0.2.0`
- [ ] (rinviati) USB-HDD parent check, `WM_DEVICECHANGE`, colonne Share/Access, export CSV, log diagnostico

## 0.3.0 — usabilità (in corso)

- [x] Icona app: lucchetto blu generato via codice, sfondo trasparente, 16/32/48/256 PNG-in-ICO; `ApplicationIcon` + `SetupIconFile`
- [x] Ricerca/filtro holders (sottostringa processo/file), copia percorso, "Apri percorso" (menu riga)
- [x] Persistenza: tema/lingua/colonne in `%APPDATA%/FluentSblock/settings.json` (+ `--lang=` vince); resx `FilterHint/MenuCopyPath/MenuOpenLocation/MenuAutostart`
- [x] Autostart opzionale (menu Visualizza, chiave HKCU Run) + AUMID `FluentSblock` per taskbar
- [x] Harness: settings roundtrip ok; smoke GUI ok
- [x] Pacchetto MSIX firmato (`dist/FluentSblock.msix` 67 MB, cert autofirmato `CN=FluentSblock` 5 anni, loghi+splash generati, `installer/FluentSblock.cer`); `signtool verify` ok dopo trust; setup+exe in `dist/` riaggiornati alla build corrente
- [ ] Sblocco utente: cert in TrustedPeople (admin) + doppio clic msix + avvio da Start
- [ ] Test manuale: filtro, copia/apri, cambio lingua persistente, autostart on/off, icona visibile

## 1.0.0 — stabile

- [x] Valutazione migrazione WinUI 3: RESTA WPF. Niente benefici misurabili (UI già Fluent-manuale, Mica via DWM, per-user xcopy senza packaging); WinUI costerebbe packaged/MSIX obbligatorio + drop Windows 10 1809-1909. Rivalutare solo se serve Store.
- [x] Suite test: `src/FluentSblock.Tests` xUnit, golden fixture vs `handle64.exe -v` (passato: fixture trovata da entrambi in 24 s)
- [x] Accessibilità: mnemonici `_` già in 5 lingue, F5 + Ctrl+L focus campo, nessun solo-mouse path (menu contestuale anche da tastiera via tasto Menu? verifica manuale)
- [ ] Accessibilità audit (Narrator, contrasto, solo-tastiera) — manuale
- [ ] EFIGS review madrelingua, guida utente nelle 5 lingue — madrelingua

## Note / decisioni

- 2026-10-28: scelto WPF .NET8 invece di WinUI3-packaged per build immediata; `Services/` isolati per riuso futuro. (v0.1.0)
- 2026-10-28: niente chiusura handle e niente driver kernel in v0.x — sola lettura. (sicurezza)
