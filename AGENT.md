# FluentSblock — AGENT.md

Specifiche per agenti di coding. Sorgente di verità per scopo, architettura, UI, versionamento.

## 1. Identità

- Nome: `FluentSblock` (working title: "chi blocca l'USB?")
- Versione corrente: `0.2.0` — vedi `VERSION` (single-line `X.Y.Z`).
- Repo/dir: `Z:\Lavorazioni\2026\AI\FluentSblock` (NAS lento) + spec esistente `handle64-spec.md`.
- Stack v0.1: **C# .NET 8 WPF** (non WinUI3 packaging) con styling Fluent 2 manuale.
  Motivo: compila con solo SDK 8.0.425 installato, nessun workload extra, exe singolo testabile.
  Migrazione a WinUI 3 / WindowsAppSDK valutabile in `1.0.0` senza cambiare `Services/`.
- Target OS: Windows 10 1809+ / Windows 11 x64. Solo x64 (`x64` in csproj).

## 2. Scopo (cosa fa)

1. Per ogni **disco rimovibile/espellibile** (`DRIVE_REMOVABLE` + `DRIVE_FIXED` su bus USB/hotplug, es. SSD in enclosure USB — fix 0.1.1):
   elenca **quali processi tengono file aperti** su quel volume e quindi impediscono l'espulsione sicura.
2. Campo scrivibile: dato un **percorso file/cartella arbitrario**, dice **chi lo tiene locked** (processo, PID, utente, handle).
3. Menu standard + lingue **EFIGS** (en, fr, it, de, es). Lingua default = UI di Windows, fallback `en`.

Non fa: chiusura handle altrui (`handle -c` like), installazione driver kernel.
Solo lettura + espulsione sicura via API di sistema (con conferma se holder>0).

## 3. Principi UI Windows 11 applicati (da `learn.microsoft.com/windows/apps/design/`)

- Design principles: Effortless, Calm, Personal, Familiar, Complete+Coherent.
- Signature experiences mappate:
  - Geometry: finestre/dialog `8px`, controlli `4px`, no rounding se maximized/snapped. Token in `Themes/FluentTokens.xaml`: `ControlCornerRadius=4`, `OverlayCornerRadius=8`.
  - Layering: base layer (navi/comandi) + content layer (card risultati). Non più di 2 livelli.
  - Materials: `Mica` backdrop via DWM (`DwmSetWindowAttribute DWMWA_SYSTEMBACKDROP_TYPE=2`) quando disponibile, fallback solido `#F3F3F3`/`#202020`.
  - Colore: neutro + `AccentBrush` di sistema (DWM, fallback `#0078D4`); barra accento 3px, pallino stato verde/ambra/rosso, conteggi in accento; light/dark/system + high-contrast obbligatorio.
  - Tipo: `Segoe UI Variable` (fallback `Segoe UI`), `TextStyles` standard, no font custom.
  - Icona: vettoriali inline stile SVG (`Path`/`Polygon` 24px in `Themes/FluentTokens.xaml`, `x:Shared=False`, ereditano `Foreground`), zero font/dipendenze.
  - Motion: solo animazioni implicite WPF, durata ≤167ms, disattivabili (`SystemParameters`).
  - Input: mouse+tastiera+touch completi, scorciatoie `F5` refresh, `Ctrl+L` focus campo file, `Alt+` menu.

## 4. Layout finestra principale (`MainWindow.xaml`)

```
MenuBar: File | Visualizza | ?/Help  (File>Aggiorna,Espelli,Export CSV/TSV,Esci; Visualizza>Tema,Lingua,Avvio automatico; Help>Mostra log,About)
Toolbar: [Drive ComboBox] [Refresh F5] [Eject (disabled v0.1)] | Theme | Lang — bottoni con icona+testo, menu con icone, drive con icona cartella
Split:
  Left: ListView dischi rimovibili (lettera, label) — empty state: "Nessun disco rimovibile"
  Right: DataGrid lock holders (Processo, PID, Utente, Handle, Accesso, File, ordinabile) — empty state: "Nessun blocco"
Bottom card: "Controlla file" — TextBox percorso + [Sfoglia...] + [Chi tiene bloccato?] + risultati; splitter trascinabile con area risultati
StatusBar: stato scansione, conteggio handle, "Esegui come amministratore" se non elevato, versione
```

Responsive: min 800x500, `NavigationView`-like a 2 colonne che collassa a 1 sotto 700px. Tutti i testi da `.resx`, mai hardcoded.

## 5. Architettura / file

```
VERSION                         # "0.2.0"
dist/                           # release: Setup.exe + msix + exe + VERSION.txt + LEGGIMI.txt
installer/                      # FluentSblock.iss (Inno per-utente) + build-setup.bat + FluentSblock.cer (pubblico)
AGENT.md                        # questo file
TODO.md                         # tracciamento attività
handle64-spec.md                # riferimento enumerazione handle (Sysinternals)
src/FluentSblock/
  FluentSblock.csproj           # net8.0-windows, UseWPF, x64, Version 0.2.0
src/FluentSblock.Tests/
  FluentSblock.Tests.csproj     # xUnit golden: fixture lock vs handle64 -v
  App.xaml[.cs]                 # tema, lingua, DWM Mica
  MainWindow.xaml[.cs]          # layout §4, MVVM leggero code-behind in v0.1
  Models/
    RemovableDrive.cs           # Letter, Label
    LockHolder.cs               # ProcessName, Pid, User, HandleHex, FilePath, Type
  Services/
    DriveEnumerator.cs          # GetLogicalDrives + GetDriveTypeW + GetVolumeInformationW
    HandleEnumerator.cs         # NtQuerySystemInformation + DuplicateHandle + NtQueryObject (cfr §6)
    FileLockChecker.cs          # Restart Manager RmStartSession/RegisterResources/GetList (per campo file)
    Log.cs                      # log diagnostico append-only in %TEMP%
    AppSettings.cs              # tema/lingua/colonne in %APPDATA% (JSON, mai throw)
    EjectService.cs             # CM_Request_Device_EjectW bottom-up + veto localizzato + PreviewEjectTarget
  Themes/FluentTokens.xaml      # corner radius, brush, TextStyles
  Resources/Strings.{en,fr,it,de,es}.resx  # EFIGS, stesse chiavi
```

MVVM: v0.1 code-behind + `ObservableCollection` basta. Introdurre CommunityToolkit.MVVM solo se la finestra supera ~400 righe.

## 6. Enumerazione handle (regole, da `handle64-spec.md §6`)

- `EnablePrivilege(SeDebugPrivilege)`. Senza admin: elenca solo propri processi, mostra banner "non elevato".
- `SetThreadErrorMode(SEM_FAILCRITICALERRORS)` a inizio scansione (come handle64 main: niente dialoghi toccando tutti i processi).
- `NtQuerySystemInformation(SystemExtendedHandleInformation=64)` via `GetProcAddress(ntdll)`: prima probe con buffer NULL per dimensione esatta (come `FUN_140007a40`), poi loop realloc su `STATUS_INFO_LENGTH_MISMATCH`. Fallimento → errore localizzato `ScanFailedHandles`, mai lista vuota silenziosa. Fallback a `SystemHandleInformation=16`.
- Per PID: `OpenProcess(PROCESS_DUP_HANDLE|QUERY_INFORMATION|QUERY_LIMITED_INFORMATION)`. Se fallisce → skip, non errore fatale.
- Per handle: `DuplicateHandle(DUPLICATE_SAME_ACCESS)` poi `NtQueryObject(ObjectTypeInformation)` — tieni solo `File`; `NtQueryObject(ObjectNameInformation)` per path NT (`\Device\HarddiskVolumeN\...`).
- Conversione NT→DOS: `QueryDosDeviceW` per ogni lettera + cache, match longest-prefix-first; `\Device\Mup\...` convertito in UNC `\\server\share...`. Per path/lettere di rete: forme equivalenti via `WNetGetUniversalName` (DOS + NT + UNC + MUP confrontate tutte).
- Filtro volume: path DOS `startswith "E:\"` (case-insensitive). Per campo file singolo: match esatto + match directory-prefix.
- Utente: `OpenProcessToken(TOKEN_QUERY)` sull'handle processo già aperto + `GetTokenInformation(TokenUser)` + `LookupAccountSidW`, fallback `""`, cache per PID.
- Performance: scansione async con `CancellationToken`, `IProgress`, query nomi su pool con timeout 100 ms (evita hang su pipe). Nomi/utenti risolti una volta per PID dall'handle già aperto.
- Restart Manager (per `FileLockChecker`): `RmStartSession/RmRegisterResources/RmGetList` — path ufficiale, funziona senza `SeDebug` per molti casi. Usa questo per il campo file, handle-enumeration per la scansione volume.

Divieti: nessuna scrittura SYS/driver, nessuna chiusura handle, nessuna chiave `Services`, nessuna rete.

## 7. Localizzazione EFIGS

- Chiavi in `Strings.en.resx` = master. `fr/it/de/es` stesse chiavi, traduzione completa prima di ogni release minor.
- Formati data/numero via `CultureInfo.CurrentUICulture`. Test: avvia con `--lang=fr` (override) e verifica nessun fallback visibile.
- Menu standard (etichette indicative, tradurre):
  - File: Refresh/Actualiser/Aggiorna/Aktualisieren/Actualizar, Eject, Exit
  - View/Affichage/Visualizza/Ansicht/Vista: Theme, Language/Langue/Lingua/Sprache/Idioma
  - Help/Aide/Aiuto/Hilfe/Ayuda: About/À propos/Informazioni/Info/Acerca de

## 8. Versionamento X.Y.Z

- File `VERSION` una riga, es. `0.1.0`. `csproj.Version` = stesso valore. StatusBar mostra stesso valore.
- `MAJOR` (X): breaking (cambio formato output, drop OS, passaggio WinUI).
- `MINOR` (Y): feature (eject, kill-handle, nuove lingue oltre EFIGS).
- `PATCH` (Z): fix, testi, styling senza feature.
- Si parte da `0.1.0`. Ogni modifica utente-visibile → bump + riga in `TODO.md` + tag `vX.Y.Z`.
- Pre-1.0: API interne libere di cambiare, UI stabile nelle stringhe EFIGS (no rename chiavi senza migrare i 5 resx).

## 9. Workflow per agenti (obbligatorio)

- NAS lento: **non** buildare/restorare su `Z:`. Sorgenti su NAS, **tutti i generati su SSD**:
  ```
  dotnet build src/FluentSblock/FluentSblock.csproj `
    --artifacts-path C:/Temp/opencode/FluentSblock-artifacts `
    -c Debug
  ```
  `NUGET_PACKAGES` resta default (`C:\Users\...\nuget\packages` su SSD). Non committare `bin/obj`.
- Verifica minima prima di dire "fatto": `dotnet build` verde + `dotnet test src/FluentSblock.Tests` verde + screenshot/lista file + `VERSION` coerente con csproj.
- Release in `dist/`: `dotnet publish src/FluentSblock/FluentSblock.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false --artifacts-path C:/Temp/opencode/FluentSblock-publish`, smoke test dell'exe su SSD, poi copia exe + `VERSION.txt` + `LEGGIMI.txt` in `dist/` e pulisci il temp.
- MSIX: tool da NuGet `Microsoft.Windows.SDK.BuildTools` (makeappx+signtool x64, niente SDK admin); chiave privata in `%USERPROFILE%\certs\FluentSblock.pfx` (pwd `FluentSblock01`, MAI nel repo), pubblico in `installer/FluentSblock.cer`; manifest `0.2.0.0`/`CN=FluentSblock`; pack → `signtool sign -fd SHA256` → `signtool verify -pa` (fallisce finché il cert non è in TrustedPeople).
- Non creare file oltre quelli in §5 senza necessità. Preferire edit a nuovi file.
- Test manuali: (a) nessuna USB → empty state; (b) USB con file aperto in Notepad → riga attesa; (c) campo file con `C:\...` locked → stesso PID; (d) cambio lingua a ciascuna EFIGS senza crash.
- Lingua di risposta all'utente: italiano. Commenti codice: inglese, concisi.

## 10. Definition of Done v0.1.1

- [x] Build verde con artifacts su SSD
- [x] Finestra apre, elenca dischi espellibili incl. USB-Fixed (verificato E: via harness)
- [ ] Finestra aperta da `run-debug.bat` mostra E: (test manuale utente)
- [ ] Refresh mostra holder reali per volume (verificato con file aperto)
