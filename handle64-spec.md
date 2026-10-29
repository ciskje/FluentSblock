# handle64.exe (Sysinternals Handle v5.0) — Specifica funzionale dettagliata

Origine analisi: `C:/Temp/opencode/handle64.exe` (copia locale di lavoro).
Target: Sysinternals Handle viewer v5.0, Mark Russinovich, 1997-2022.
Metodo: REA 6.8.0 (npx, identico alla build locale ricostruita in `Z:\Lavorazioni\2026\AI\rea`):
inspect-artifact, inspect-pe-imports (anche sul driver estratto), inspect-pe-resources,
inspect-managed-artifact, inspect-go-binary, inspect-rust-binary, inspect-byte-entropy,
inspect-native-exports, inspect-pe-load-config, inspect-pe-certificates,
inspect-with-objdump (file_headers/section_headers) + `strings`/`objdump` mingw64 2.46
+ esecuzione diretta con vari flag.
Decompilazione profonda NON disponibile su questo host: Ghidra non configurato
(`GHIDRA_INSTALL_DIR` assente), Hopper `unsupported_host` su win32, IDA/Kuna non
configurati, `capture-process` non supportato su Windows (richiede PTY Linux/macOS).
Quindi sotto: osservato (statico/runtime) vs inferito vs ignoto sono separati.

## 1. Identità

- File: `handle64.exe`, 416144 byte, SHA256 `24bafcc570cc9bbb6b6e6652a57a519e0464e3996891aaba6f55299cce20b04f`.
- PE32+ x86-64, Machine `0x8664`, 7 sezioni, Entry `0x14000a610`, ImageBase `0x140000000`.
- Linker 14.33 (VS2022), TimeDate `2022-10-25 17:55:13`, Subsystem `Windows CUI (3)`.
- DllCharacteristics: `HIGH_ENTROPY_VA|DYNAMIC_BASE|NX_COMPAT|TERMINAL_SERVICE_AWARE`.
- LoadConfig 320 byte con `IMAGE_GUARD_CF_INSTRUMENTED` (claim linker, non verificato a istruzioni).
- Security cookie in `.data`, GuardCF check/dispatch pointer in `.rdata`, nessun TLS callback.
- Firma Authenticode PKCS#7 (10128 byte): leaf `Microsoft Corporation` (PCA 2011, serial `33000002cc8eb596a6bdd1c94e…`), digest SHA-256. Solo identità, nessuna verifica firma eseguita qui.
- Version resource: FileVersion/ProductVersion `5.0`, `Handle viewer`, `Sysinternals - www.sysinternals.com`.
- Banner runtime: `Nthandle v5.0 - Handle viewer / Copyright (C) 1997-2022 Mark Russinovich / Sysinternals - www.sysinternals.com`.
- PDB user-mode: `D:\a\1\s\x64\Release\Handle64.pdb`. PDB driver embedded: `D:\a\1\s\modules\procexp\sys\x64\Release\ProcExpDriver.pdb`.
- `inspect-managed-artifact`: `not-managed` (no CLI directory). `inspect-go-binary`: nessun record Go. `inspect-rust-binary`: nessuna sezione `.dep-v0` (non Rust). Entropia file 6.2957, nessun packer (sezioni standard MSVC, import intatta, `UPX/packed` assente).
- Export table assente (0 export). Delay-import assente.
- Entry `0x14000a610` confermato via `inspect-with-objdump file_headers` (flags `HAS_RELOC, EXEC_P, HAS_LINENO, HAS_DEBUG, HAS_LOCALS, D_PAGED`); ogni ispezione REA riporta `artifact_changed: false` (target mai modificato).

Sezioni (objdump -h):

| Nome | Size | VMA | File off | Flags |
|---|---|---|---|---|
| `.text` | 0x36114 (221460) | 0x140001000 | 0x400 | CODE READONLY |
| `.rdata` | 0x1cb90 (117648) | 0x140038000 | 0x36600 | DATA READONLY |
| `.data` | 0x3000 (12288) | 0x140055000 | 0x53200 | DATA RW |
| `.pdata` | 0x2994 (10644) | 0x140065000 | 0x56200 | exception |
| `_RDATA` | 0x15c (348) | 0x140068000 | 0x58c00 | DATA |
| `.rsrc` | 0x9738 (38712) | 0x140069000 | 0x58e00 | resource |
| `.reloc` | 0xad8 (2776) | 0x140073000 | 0x62600 | reloc |

## 2. Architettura

```
handle64.exe (CUI user-mode)
 ├─ CLI parser + output formatter (printf wide osservati)
 ├─ Enumeratore user-mode: NtQuerySystemInformation + NtQueryObject + OpenProcess/DuplicateHandle
 ├─ Risolutore nomi: NtQueryObject, NtQuerySymbolicLinkObject, GetFullPathNameW, GetDriveTypeW
 ├─ Risolutore utente: OpenProcessToken + GetTokenInformation + LookupAccountSidW
 ├─ Privilegi: SeDebugPrivilege, SeLoadDriverPrivilege via AdjustTokenPrivileges
 └─ Fallback kernel: driver embedded PROCEXP152.SYS (BINRES 103, 37280 byte) → Service + NtLoadDriver + \\.\Global\PROCEXP152 + DeviceIoControl
```

Driver embedded: risorsa `BINRES` id 103 (lang 1033), offset file 365080, 37280 byte,
SHA256 `ff9b3fc49bb3cd9a2ffea2dd8075a34908346fb8393aa2bf13aa15ac72583928`,
magic `MZ`, PDB ProcExpDriver. Estratto in
`C:/Temp/opencode/handle-BINRES.bin` per ispezione indipendente.
È un driver kernel reale, non un blob dati:
- `objdump -p`: Subsystem `NT native (1)`, ImageBase `0x180000000` (base driver standard), Entry `0x9000` → `0x180009000` (DriverEntry in sezione INIT), SizeOfImage `0xc000`, CheckSum `0x18b29`, Debug directory presente.
- Sezioni (`inspect-with-objdump section_headers`): 8, con `PAGE` (0x1c53, codice paginabile) e `INIT` (0x87e, init scartabile con DriverEntry); `.text/.rdata/.data/.pdata` con flag `IMAGE_SCN_MEM_NOT_PAGED` (core residente — tipico driver).

| Nome | Size | VMA | Flags |
|---|---|---|---|
| `.text` | 0x2140 | 0x180001000 | CODE, NOT_PAGED |
| `.rdata` | 0xfd4 | 0x180004000 | DATA, NOT_PAGED |
| `.data` | 0x26c | 0x180005000 | DATA, NOT_PAGED |
| `.pdata` | 0x2e8 | 0x180006000 | exception, NOT_PAGED |
| `PAGE` | 0x1c53 | 0x180007000 | CODE paginabile |
| `INIT` | 0x87e | 0x180009000 | CODE init (DriverEntry) |
| `.rsrc` | 0x380 | 0x18000a000 | DATA |
| `.reloc` | 0x3c | 0x18000b000 | DATA |

- Import esatti (`inspect-pe-imports` sul driver): unica DLL `ntoskrnl.exe`, 66 funzioni, raggruppate per ruolo:
  - Enumerazione handle: `ZwQuerySystemInformation, ZwQueryObject, ZwDuplicateObject, ZwQueryInformationProcess, ObReferenceObjectByHandle, ObOpenObjectByPointer, ObOpenObjectByName, ObQueryNameString, ObCloseHandle, ObfDereferenceObject, ZwOpenProcess, ZwOpenProcessToken, ZwClose, PsLookupProcessByProcessId, MmIsAddressValid`.
  - Attach a processi protetti: `KeStackAttachProcess, KeUnstackDetachProcess, KeWaitForSingleObject, KeLowerIrql, KfRaiseIrql, KeBugCheckEx`.
  - Device sicuro: `IoCreateDevice, IoCreateSymbolicLink, IoDeleteDevice, IoDeleteSymbolicLink, IofCompleteRequest, IoFileObjectType, IoDeviceObjectType, IoIsWdmVersionAvailable, SeCaptureSecurityDescriptor, SeCaptureSubjectContext, SeReleaseSubjectContext, SePrivilegeCheck, RtlCreateSecurityDescriptor, RtlSetDaclSecurityDescriptor, RtlAddAccessAllowedAce, RtlAbsoluteToSelfRelativeSD, RtlLengthSecurityDescriptor, RtlGetDacl/OwnGroup/Owner/SaclSecurityDescriptor, SeExports`.
  - Persistenza service: `ZwCreateKey, ZwSetValueKey, ZwQueryValueKey, ZwOpenKey, ZwSetSecurityObject`.
  - Utilità: `RtlInitUnicodeString, RtlUnicodeStringToAnsiString, RtlFreeUnicodeString, RtlFreeAnsiString, ExAllocatePoolWithTag, ExFreePoolWithTag, ExGetPreviousMode, MmGetSystemRoutineAddress, PsGetVersion, PsInitialSystemProcess, PsProcessType, PsThreadType, strncpy, wcschr, _wcsnicmp, _snwprintf, __C_specific_handler`.
- Stringhe dispositivo: `\Device\PROCEXP152`, `\DosDevices\PROCEXP152`, `IoCreateDeviceSecure`, `IoValidateDeviceIoControlAccess`.

## 3. Import statici (148 funzioni, 6 DLL)

Nessun import statico `ntdll.dll` / `Nt*` / `Zw*`. Prova di caricamento dinamico:
`LoadLibraryExW + GetModuleHandleW/ExW + GetProcAddress` importati da KERNEL32.

- KERNEL32.dll (117): `GetDriveTypeW, GetFullPathNameW, DuplicateHandle, DeviceIoControl, SetThreadErrorMode, CreateEventW, GetCurrentProcessId, CreateProcessW, OpenProcess, GetSystemDirectoryW, FormatMessageW, FindFirstFileExW/FindNextFileW/FindClose, CreateFileW, CloseHandle, LocalAlloc/Free, HeapAlloc/Free/ReAlloc, GetModuleHandleW/ExW, GetProcAddress, LoadLibraryExW, GetCommandLineW/A, GetStdHandle, GetModuleFileNameW, WriteConsoleW/WriteFile, ReadFile/ReadConsoleW, GetConsoleMode/ScreenBufferInfo, VerifyVersionInfoW/VerSetConditionMask/GetVersionExW, ExpandEnvironmentStringsW, GetEnvironmentStringsW, WideCharToMultiByte/MultiByteToWideChar, Enter/Leave/DeleteCriticalSection, Encode/DecodePointer, RtlCaptureContext/RtlLookupFunctionEntry/RtlVirtualUnwind/RtlUnwind*, CreateThread/ExitThread/TerminateThread, TerminateProcess, Tls*/Fls*, ...`
- USER32.dll (9): `SendMessageW, DialogBoxIndirectParamW, EndDialog, LoadCursorW, InflateRect, GetSysColorBrush, SetCursor, SetWindowTextW, GetDlgItem` (dialoghi EULA/stampa).
- GDI32.dll (6): `StartDocW, StartPage, EndPage, EndDoc, SetMapMode, GetDeviceCaps` (stampa).
- COMDLG32.dll (1): `PrintDlgW`.
- ADVAPI32.dll (12): `OpenProcessToken, GetTokenInformation, LookupAccountSidW, LookupPrivilegeValueW, AdjustTokenPrivileges, RegOpenKeyExW/RegQueryValueExW/RegSetValueExW/RegCreateKeyW/RegDeleteKeyW/RegCloseKey`.
- VERSION.dll (3): `GetFileVersionInfoSizeW/InfoW, VerQueryValueW`.

Nomi funzione risolti a runtime via `GetProcAddress` (stringhe ANSI osservate):
`NtLoadDriver, NtOpenDirectoryObject, NtOpenSymbolicLinkObject, NtQueryDirectoryObject, NtQueryInformationProcess, NtQueryInformationThread, NtQueryObject, NtQuerySection, NtQuerySymbolicLinkObject, NtQuerySystemInformation, RtlNtStatusToDosError, ZwClose, ZwCreateKey, ZwDuplicateObject, ZwOpenKey, ZwOpenProcess, ZwOpenProcessToken, ZwQueryInformationProcess, ZwQueryObject, ZwQuerySystemInformation, ZwQueryValueKey, ZwSetSecurityObject, ZwSetValueKey`.
Nome DLL passato come wide: `ntdll.dll` (unica occorrenza wide + `kernel32.dll`).

Inferenza (non osservato in decompilazione): `GetModuleHandleW(L"ntdll.dll")` o
`LoadLibraryExW`, poi `GetProcAddress` per ciascuna sopra; fallback `ZwQuerySystemInformation`
se `NtQuerySystemInformation` manca (alias storico).

## 4. CLI

Sintassi (da `-?`, osservata byte-identica):

```
handle [[-a [-l]] [-v|-vt] [-u] | [-c <handle> [-y]] | [-s]] [-p <process>|<pid>] [name] [-nobanner]
```

| Flag | Significato osservato |
|---|---|
| (nessuno) | Dump di tutti i riferimenti file (solo tipo File). Verificato: `-nobanner` da solo elenca File di ogni processo. |
| `-a` | Dump di TUTTI gli handle (tutti i tipi). |
| `-l` | Solo Section pagefile-backed; richiede `-a`. Output aggiunge riga `Pagefile %I64d bytes`. Verificato su `opencode.exe`. |
| `-v` | CSV con virgola. Header `Process,PID,User,Handle,Type,Share Flags,Name`. Handle in `0x%08X`. |
| `-vt` | CSV con TAB (`\t`, osservato `^I`). Stesso header, separatore tab. CRLF (`\r\n`). |
| `-u` | Mostra `DOMAIN\user` proprietario dopo `pid:`. Richiede `OpenProcessToken`; fallback `<unable to open process>`, `<unknown owner>`, `<unable to open token>`. |
| `-s` | Riepilogo conteggi per tipo + `Total handles: %d`. Non combinabile con `-a/-v/-c` (gruppi mutualmente esclusivi da usage). |
| `-c <handle>` | Chiude handle (numero esadecimale). Richiede `-p <PID numerico>` (non nome). Richiede admin. Prompt `Close handle %X in %s (PID %d)? (y/n)` salvo `-y`. Messaggi: `Handle closed.` / `Error closing handle:` / `Handle close aborted.` Warning: instabilità possibile. Verificato: con PID inesistente (`-c 1234 -p 999999`) stampa `No matching handles found.` con exit 0 senza toccare nulla (path sicuro). |
| `-y` | Solo con `-c`: salta conferma. |
| `-g` | Stampa granted access sotto ogni riga (mask simboliche tipo `READ_CONTROL|DELETE|...|EVENT_ALL_ACCESS` o esadecimale `0x%04X` / `0x0001` per tipi senza mappa). |
| `-p <process\|pid>` | Filtra per nome parziale (case-insensitive, accettato parziale) o PID. Filtro senza match → `No matching handles found.` su stdout, exit 0 (verificato a runtime con `-p NOMATCHZZZ`). |
| `name` | Sottostringa (fragment accepted) cercata nel nome oggetto. Posizionale finale. Combinabile con `-p`. Esempio verificato: `handle opencode.db -nobanner` → solo 4 righe File `294/298/29C/2A0`. |
| `-nobanner` | Nasconde banner+copyright. Senza, banner su stdout prima dell'output. |
| `-accepteula` / EULA | Stringhe osservate: `Use -accepteula to accept EULA.`, chiave `Software\Sysinternals`, first-run dialog. Non testato interattivamente qui (EULA già accettata sulla macchina). |
| `-?` / `-h` / args invalidi | Stampa usage + `No arguments will dump all file references.` Exit 0. `-c` senza PID ricade su usage. |

Gruppi esclusivi da usage: `{[-a [-l]] [-v|-vt] [-u]} | {[-c [-y]]} | {[-s]}` + modificatori globali `-p [name] [-nobanner]`.

Exit code osservato: 0 in tutti i run di successo e anche su usage/help.

## 5. Formati output (osservati + pattern printf nelle stringhe wide)

Separatore processo:
```
------------------------------------------------------------------------------
System pid: 4 \NT AUTHORITY\SYSTEM
------------------------------------------------------------------------------
opencode.exe pid: 16616 TITAN\Principale
```

Default (solo File, senza `-a`): una riga per handle:
```
opencode.exe       pid: 16616  type: File           294: C:\Users\...\opencode.db
```
Pattern: `%-18s pid: %-6d type: %-13s %-25s %0x08X %s%s%s%s` (famiglia). Handle in esadecimale senza `0x`, maiuscolo, non zero-padded nel default (`294`), con `0x%08X` in CSV/close.

`-a` (tutti i tipi): indentato sotto header processo:
```
    4: Event
    C: Key           HKLM\SOFTWARE\...\Image File Execution Options
   B4: Thread        opencode.exe(16616): 8840
   A8: File          \Device\CNG
```
Tipi senza nome stampano solo tipo (es. `Event` vuoto). `<Unknown type>` per tipi non mappati (osservati molti su builds recenti: `WaitCompletionPacket, IRTimer, TpWorkerFactory, SchedulerSharedData, ...`). `<Nonexistent process>`, `<unable to open process>`, `<Unknown type>`, `0x%04X` per fallback.

`-g`: seconda riga mask:
```
    4: Event
       READ_CONTROL|DELETE|SYNCHRONIZE|WRITE_DAC|WRITE_OWNER|EVENT_ALL_ACCESS
   10: WaitCompletionPacket
       0x0001
```
Tabelle simboliche osservate: `READ_CONTROL/DELETE/SYNCHRONIZE/FILE_ALL_ACCESS/PROCESS_ALL_ACCESS/TOKEN_ALL_ACCESS/...` + `QUERY_VALUE|ENUMERATE_SUB_KEYS` (Key), `QUERY|TRAVERSE` (Directory), `0x0804` per Unknown.

`-s`: conteggi (run reale, 123842 handle totali su questa macchina):
```
Handle type summary:
  <Unknown type>  : 7
  ...
  ALPC Port       : 4634
  Desktop         : 573
  Directory       : 551
  DxgkSharedResource: 219
  Event           : 26489
  File            : 7701
  ...
Total handles: 123842
```
Nota: `<Unknown type>` appare con più righe distinte (sottotipi non distinti dal tool — limite noto su Windows 10/11 per nuovi tipi kernel). Header esatto `Handle type summary:` poi `  %-16s: %d`, poi `Total handles: %d`, oppure `No matching handles found.` se filtro vuoto.

`-v` CSV (virgola), `-vt` TSV (tab). Header identico, campi: `Process,PID,User,Handle,Type,Share Flags,Name`. `Share Flags` quasi sempre vuoto per File. Nome in forma Win32 (`C:\...`) o NT (`\Device\Mup\192.168.1.11\...` per share NAS — osservato perché working dir su NAS). Handle `0x00000188` zero-padded 8 cifre.

`-l`: come `-a` ma solo Section + riga `Pagefile N bytes` quando backed.

`-u`: aggiunge ` DOMAIN\user` su riga header processo; senza `-u` solo `pid:` (default e `-a` senza utente mostrano comunque owner? Osservato: `-a -p opencode` senza `-u` mostra già `TITAN\Principale` — quindi `-u` forza risoluzione anche in modalità search; comportamento esatto: con filtro nome mostra owner di default).

Errori osservati nelle stringhe: `Error obtaining handle information:`, `Access denied`, `Insufficient system resources`, `Handle requires Windows XP or higher.`, `Unable to extract x64 image. Run Handle from a writeable directory.` (selettore x86/x64 self-extract).

## 6. Enumerazione (ricostruzione inferita, coerente con import/stringhe/output)

Confermato via Ghidra (funzioni sotto, nomi `FUN_` = simboli auto-generati, binario strippato):

1. `main` (`FUN_140009100`, chiamato da `__scrt_common_main_seh`): parse CLI → resolver `FUN_140007a40` (se fallisce: errore e exit) → abilita `SeDebugPrivilege` (stringa letterale) → `SetThreadErrorMode(SEM_FAILCRITICALERRORS)` per non far apparire dialoghi toccando tutti i processi → loop `FUN_140006190` → check `VerifyVersionInfoW(major 6)` per comportamento Win10+.
2. Il resolver, oltre a risolvere i puntatori, sonda `NtQuerySystemInformation(0x40, NULL, 0, NULL)` e accetta solo se lo status indica buffer insufficiente (`STATUS_INFO_LENGTH_MISMATCH`): la classe usata è **0x40 = `SystemExtendedHandleInformation`**, non la vecchia 16.
3. `FUN_140007980` alloca e riempie lo snapshot (loop di realloc); `FUN_1400078f0` = conteggio entry; `FUN_140007900(i)` = entry i-esima. Layout entry osservato: a offset +0x1e l'indice tipo (ushort) che indicizza una tabella nomi interna a stride 0x80 (`DAT_14005cf00`).
4. A ogni cambio PID: `FUN_140007d60(pid)` = `OpenProcess` (se fallisce → header con `<unable to open process>`), `FUN_140007f20` = nome exe del processo. Poi per ogni handle `FUN_140007180` = `DuplicateHandle` nel proprio processo + `NtQueryObject` tipo/nome.
5. Nomi: per `Key` chiama `FUN_140008250` che abbrevia i prefissi registry (le cui lunghezze sono precalcolate a inizio loop: `HKCU/HKLM/HKCR...`); per gli altri `FUN_140008120` converte `\Device\...` in lettera di drive. In modo default (senza `-a`) i nomi che restano in forma `\Device...` vengono **scartati**, tranne `\Device\Mup` (condivisioni di rete, mostrate così come sono) — verificato nel flusso con `_wcsnicmp`.
6. Filtri nel loop: match case-insensitive del frammento con `_wcsnicmp` sul nome oggetto; in default solo tipi `file`/`section` (`_wcsicmp`), con `-a` tutti, con `-l` solo `Section`. Flag interni osservati per comportamento: `-a`, `-l`, `-g` (stampa `GrantedAccess` dell'entry via `FUN_140003ee0`), `-p` PID, frammento nome.
7. Fallback driver: invariato rispetto a prima (import driver confermati: `ZwQuerySystemInformation`, `KeStackAttachProcess`, `ObQueryNameString`, ...).

Ignoto (serve kernel debugging/decompilazione): codici IOCTL esatti verso PROCEXP152 e struct di scambio kernel/user; il resto del flusso driver è ora coperto da import+sezioni.

## 7. Requisiti e side-effect per reimplementazione

- OS: Windows XP+ (dichiarato), testato qui su Windows 11 x64. Solo x64 (`handle64.exe`; selettore x86→x64 via `64.exe` + `%TEMP%`).
- Privilegi: user normale elenca propri processi; `SeDebugPrivilege` per sistema/altri utenti; admin + `SeLoadDriverPrivilege` per driver e `-c`.
- Side-effect: nessun file scritto in modalità normale; con fallback driver: scrittura SYS in TEMP/Drivers + chiave Services + load driver (richiede consenso admin). `-c`: chiude handle altrui → instabilità.
- Nessuna rete (solo `\Device\Mup` naming per share esistenti). Nessun export API.
- EULA Sysinternals al primo run (`-accepteula` per silent).

## 8. Mini-mappa per reimplementare (C/C++/C#)

- Parser CLI come §4 (gruppi esclusivi, `-p` parziale, `name` fragment, `-nobanner`, `-y` solo con `-c`).
- `EnablePrivilege(SeDebugPrivilege)`; per `-c`/driver anche `SeLoadDriverPrivilege`.
- `NtQuerySystemInformation` dinamico (GetProcAddress): loop realloc buffer; gestisci `SystemExtendedHandleInformation` (classe 64) con fallback a `SystemHandleInformation` (16).
- Per PID: `OpenProcess`, `DuplicateHandle`, `NtQueryObject`, `NtQuerySymbolicLinkObject`, `QueryFullProcessImageNameW` per nome exe.
- Nomi file: `GetMappedFileNameW` o `NtQueryObject` + `DosDevicePath→Drive` via `GetLogicalDrives/QueryDosDeviceW`; mantieni `\Device\Mup` se non mappabile.
- Utente: `OpenProcessToken + GetTokenInformation + LookupAccountSidW`.
- Output: replica formati §5 inclusi header CSV, `%0x08X` vs `%X`, `Pagefile %I64d bytes`, `Total handles`, separatori 78×`-`.
- `-s`: dizionario tipo→conteggio, preserva righe `<Unknown type>` distinte se vuoi parità bug-compatibile, oppure unifica (dichiara differenza).
- `-c`: `DuplicateHandle(h, DUPLICATE_CLOSE_SOURCE)` + prompt `y/n` su stdin.
- Test: confronta `-s` totali e `-v` per un processo noto (`-p <self> -v`) con golden file; tollera `<Unknown type>` e PID/TID variabili.

## 9. Provenienza evidence

- `C:/Temp/opencode/handle-artifact.json` (inspect-artifact, root PE, sha256 match).
- `C:/Temp/opencode/handle-imports.json` (6 DLL, 148 API, no NTDLL statico).
- `C:/Temp/opencode/handle-resources.json` (BINRES 37280 + VERSION + manifest).
- `C:/Temp/opencode/handle-managed.json` (not-managed), `handle-go.json` (no Go), `handle-entropy.json` (6.2957).
- `C:/Temp/opencode/handle-BINRES.bin` (driver estratto, MZ, sha `ff9b3f...`).
- `C:/Temp/opencode/drv-imports.json` (import esatti driver: solo `ntoskrnl.exe`, 66 API).
- `C:/Temp/opencode/exe-objdump-file.json`, `drv-objdump-file.json`, `drv-objdump-sec.json` (header/sezioni Evidence-backed, `artifact_changed: false`).
- `handle-rust` negativo (no `.dep-v0`) via `inspect-rust-binary`.
- Run osservati: `-?`, `-s`, `-nobanner`, `-p opencode -v/-vt/-a/-g`, `-a -l`, filtro `opencode.db`, `-p NOMATCHZZZ` (no-match verificato), `-c 1234 -p 999999` (path sicuro verificato).
- Ghidra 12.1.4 (provider `ghidra`, snapshot `C:/Temp/opencode/handle-ghidra-snap.json`): `handle-ghidra-analyze.json` (1134 procedure, 927 stringhe), `fn-resolver.json` (`FUN_140007a40`, catena `GetProcAddress` + probe classe 0x40), `fn-enum.json` (`FUN_140009100` = main), `fn-loop.json` (`FUN_140006190` = loop enumerazione, pseudocodice in `fn-*-pseudo.txt`).

Limiti dichiarati: nessuna pseudocodice/decompilazione (provider nativi non disponibili);
IOCTL e struct interne driver non recuperati; conteggi `-s` variano per macchina/carico;
nomi `<Unknown type>` dipendono da versione Windows.
