# Verifica funzionale e sicurezza — 12 ottobre 2026

Sono state eseguite tutte le 11 suite .NET disponibili su Windows, le suite di
piattaforma Linux, i controlli frontend, i test di compressione con video generati,
le verifiche API/browser e le scansioni di codice, dipendenze, segreti e immagine.
Gli errori riproducibili rilevati durante il lavoro sono stati corretti e verificati.
I casi ignorati e gli avvisi residui sono riportati separatamente: questo rapporto
non afferma che ogni possibile vulnerabilità sia esclusa.

Codice verificato: `3f428c4492e0dc691cc8974af4fe8f15515ec37c`, seguito dal
solo hardening dei permessi di packaging
`86bea3541fd78b581b4e62ded194b323e9cfc072`, sul ramo
`integration/sonarr-latest-anilist`. Base precedente: `130831c3b`.

## Problemi corretti

| Problema confermato | Correzione e prova |
| --- | --- |
| Chiave TMDB `api_key` visibile nei log HTTP | Sanitizer corretto; quattro regressioni e riproduzione sul DLL: il segreto non compare più |
| Playlist DASH camuffata da `.mkv` apriva URL nel worker opzionale | Protocolli e demuxer limitati a file e contenitori supportati in probe, encode e validazione; test con server locale verifica zero richieste |
| Token companion `add-series` poteva scegliere un percorso e avviare ricerche | Policy comune prima dell'aggiunta in v3/v5: root registrata, cartella generata, niente flag di ricerca; 20 casi unitari e 12 richieste negative senza modifiche a serie/comandi |
| Permessi DrvFs rendevano scrivibili entrypoint e asset UI | COPY con permessi espliciti e rimozione dei bit di scrittura per gruppo/altri da `/app`; verifica come UID 1000 |
| ArchivePathGuard si bloccava sulla radice del filesystem | Conservata la radice, contenimento corretto e traversal finito; watchdog prima/dopo e otto casi Linux, inclusi symlink reali |
| Ricerca AniList mostrava attribuzione/link TVDB con ID negativo | Link e attribuzione per provider; riconoscimento esistenti tramite `(metadataSource, foreignId)`; cinque regressioni e browser reale |

I file rimasti identici upstream sono stati confrontati prima di attribuire i
problemi. Sono inoltre stati aggiornati test ereditati che richiedevano il cloud
Sonarr disabilitato, selettori del vecchio frontend e assunzioni della suite di
integrazione. Le porte fisse 9010/9012 collidevano con servizi WebSocket++ locali:
il runner ora usa porte libere e verifica la propria directory `appData`. Il test
SignalR ignora correttamente notifiche Sync senza risorsa, mantenendo le asserzioni
sullo stato persistito dello spostamento.

## Test eseguiti

| Suite Windows | Superati | Saltati nel riepilogo runner |
| --- | ---: | ---: |
| Core, incluse categorie di integrazione | 6175 | 20 |
| Common, incluse categorie di integrazione | 773 | 20 |
| API | 56 | 0 |
| HTTP | 57 | 0 |
| Host | 21 | 0 |
| Update | 17 | 0 |
| Libraries | 2 | 0 |
| Windows | 28 | 2 |
| Mono | 12 | 0 |
| Integration, istanze e database temporanei | 92 | 12 |
| Automation, Chrome 154 | 6 | 0 |
| **Totale** | **7239** | **54** |

Nessun fallimento finale. Le suite registrano ulteriori casi esclusi per piattaforma
nei TRX, oltre al riepilogo console. Windows non consentiva la creazione dei due
symlink del fixture: entrambi sono stati eseguiti e superati su Linux.

Sul backend Linux Release/Posix: Core 6122, API 56, HTTP 57, Common completo 753,
Host 17, Update 17, Libraries 2 e Mono 51 superati. Windows sul container Linux è
escluso per piattaforma. Il fixture ArchivePathGuard aggiornato passa 8/8; il
sottoinsieme cloud/sanitizer nel Docker build passa 89/89. I TRX conservano gli
ignore originali, i requisiti di piattaforma e i test che richiedono credenziali o
infrastruttura esterna. Mono richiedeva una directory ApplicationData: la prima
esecuzione sul SDK vuoto è conservata; il rerun con XDG temporaneo passa.

Frontend Node 24: 16/16 test, ESLint, Stylelint, typecheck e Vite build passati.
Worker: 29/29 con FFmpeg 6.1.1, video Matroska/MP4 generati, tracce audio,
sottotitoli, capitoli e hardlink. Build Windows senza warning/errori; il build
Linux ha avuto warning MSB3026 di copia ritentata tra progetti paralleli ed è
terminato correttamente.

Le istanze Docker isolate verificano autenticazione obbligatoria, API key,
cookie, lockout, reset invalido, redirect, AllowedHosts, CORS, percorsi statici,
input invalido, scope companion, masking TMDB, SQLite e restart. AniList pubblico:
aggiunta nativa, 26 episodi, refresh, duplicati respinti, identità persistita e
ricerca completa dry run contro Torznab vuoto, senza acquisizione. Il browser
Chromium verifica login, navigazione, dettaglio/lookup AniList, attribuzione/link,
modal di ricerca e distinzione tra stato della lista, stato dell'anime e liste
personalizzate; nessun errore JavaScript o richiesta ai servizi cloud Sonarr.

## Scansioni e residui

Trivy 0.75.0: **15 CVE uniche nell'immagine**, nessuna critica; ImageSharp 3 alte
e 2 medie, Ubuntu 7 medie e 3 basse. Nessun segreto rilevato nei sorgenti filtrati,
nello snapshot Git verificato o nell'immagine. Configurazione privata, media e
file `.env` sono esclusi dalla scansione e dai report.

- `postcss-selector-parser` è stato aggiornato a 7.1.6 e tutti i controlli frontend
  sono stati ripetuti. [`braces` ha un avviso alto senza versione corretta](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm),
  presente nella toolchain di build, assente dal runtime che non include Node.
- ImageSharp 3.1.12 rimane un componente vulnerabile. I cinque advisory
  ([TIFF T4](https://github.com/advisories/GHSA-j9gm-c75j-xc9q),
  [TIFF T6](https://github.com/advisories/GHSA-jjfr-hcj7-qf5w),
  [Float TIFF](https://github.com/advisories/GHSA-j3p4-wp97-rph4),
  [BigTIFF](https://github.com/advisories/GHSA-wmxv-xphr-5c9g),
  [ICC](https://github.com/advisories/GHSA-gwg2-r3hj-4w44)) riguardano percorsi
  esclusi dal resizer Sonarr: decoder JPEG/PNG/WebP, `SkipMetadata`, nessun encoder
  TIFF/equalizzazione/lettura delle Entries ICC. Quattro prove sul DLL reale
  confermano resize normale, rifiuto TIFF/BigTIFF e rimozione ICC malformato con
  370400 byte allocati. Non equivalgono a una patch della libreria. La versione
  4.1.2 richiede una chiave SixLabors nel build Release, non disponibile qui;
  le soppressioni NuGet preesistenti non sono state usate per dichiarare pulito
  l'audit. NuGet è stato auditato su 28 progetti; la dipendenza esclusiva del
  progetto GUI Windows non compilato è stata verificata separatamente.
- I dieci CVE Ubuntu non hanno una versione corretta nel feed del distributore
  al momento della scansione; l'inventario completo è negli artefatti Trivy/SBOM.
- Semgrep Community 1.180.0: 259 regole, 4073 file, circa 99,9% delle righe parse,
  87 segnalazioni revisionate. 53 rilievi di autorizzazione non riconoscono i
  controlli ereditati/middleware; 32 riguardano SQL. Tre migrazioni upstream
  antiche (029/030/033) interpolano testo già nel database senza escaping:
  rimangono opportunità di hardening per upgrade di database molto vecchi,
  senza exploit dell'API corrente dimostrato. Gli altri SQL usano costanti,
  valori tipizzati o parametri. Il parsing XML del worker legge configurazione
  locale amministrativa; socket 0660 è intenzionale. Sei parsing parziali e
  un timeout di fixpoint limitano la copertura automatica. Il controllo separato
  del Dockerfile dopo il delta di packaging ha zero match, ma solo 33,8% di
  parsing: per il container contano anche build, Trivy e prove dei permessi reali.
- I rilievi Docker statici default-root/no-HEALTHCHECK restano nell'inventario:
  l'entrypoint inizializza i volumi e usa gosu; il processo effettivo è UID 1000,
  senza capabilities effettive, con seccomp. Il binario ffprobe distribuito è
  compilato con rete disabilitata e solo protocollo file.

## Artefatti e limiti

I report JSON/TRX, log, SBOM CycloneDX, screenshot e prove sono conservati in
`%LOCALAPPDATA%/SonarrWeebTools/security-2026-10-12`; i risultati conclusivi
sanitizzati sono copiati in `_artifacts/security-2026-10-12`.
I test riutilizzabili e i comandi sono in [docker/tests/README.md](../docker/tests/README.md).

Non sono stati usati una libreria reale, client di download, credenziali private
AniList/OAuth o encoder VAAPI. Non sono stati effettuati fuzzing completo di
binari nativi o scansione dei segreti di tutta la storia Git. Il filtro anti-fake
rimane euristico: magnet/metadati torrent illeggibili e contenuto degli archivi
hanno i limiti documentati. Le scansioni non costituiscono certificazione di
assenza di vulnerabilità.

Le impostazioni DNS-rebinding sono provate nell'istanza temporanea con AllowedHosts
esplicito. Il deploy locale mantiene la configurazione esistente, Forms e
AuthenticationRequired=Enabled, con porta pubblicata solo su 127.0.0.1:6970.

## Immagine e deploy conclusivi

Immagine `sonarr-weeb:local`, Linux amd64:
`sha256:94555df18b98fd6f7b76dc0c88b2708538c9617f4f5d08e8a3cb93a20edceb48`.
Runtime `10.0.0.32728`; smoke, 19 controlli runtime e cinque scenari Chromium
conclusivi superati sulla stessa immagine, con configurazione preservata nel deploy.
Esportazione `_artifacts/sonarr-weeb-local-linux-amd64.tar`, 128857088 byte;
SHA-256 `f5098bfff5761f3cc661679cfec05310c5efbcf2e3b6202f4f1706b2cb722c8f`.

La verifica conclusiva, l'identità dell'immagine e il checksum dell'esportazione
sono registrati in `_artifacts/security-2026-10-12/deployment-final.json`.
Il deploy locale è raggiungibile su http://localhost:6970. Configurazione e media
non sono stati sostituiti con quelli dei test.
