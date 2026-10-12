# Integrazione delle funzionalità ikatun

Confronto richiesto: [KakarottoCake main…ikatun main](https://github.com/KakarottoCake/Sonarr-Pro/compare/main...ikatun:Sonarr-Pro:main).

## Sorgenti e criterio

- Kakarotto `main`: `c104bf96ffd2ebaa18999db29196160e4a7210b0`.
- ikatun `main`: `d2e06dc0552db509db43ab347e30b4e10736680d`.
- Base comune del confronto: `72cc4e559ff7de6fa04de9a5bcac62c77b7c1a8a`.
- Base locale prima dell'integrazione: `c4bd6a625`, preservata nel ramo
  `source/integration-before-ikatun`.
- Ramo di lavoro: `integration/sonarr-latest-anilist`, già basato su Sonarr
  `v5-develop@698bb1dbb` con funzionalità Kakarotto/gaboy e correzioni AniList.

Sono integrati i comportamenti compatibili dei 28 commit del confronto,
adattando API, UI e modelli alla base attuale. Il checkpoint iniziale non cambia
file. Pin SDK, Vite, destinazioni delle immagini e organizzazione CI seguono
questa integrazione locale; non vengono riportati a versioni o account più vecchi.

## Copertura dei commit

| Commit | Funzionalità / trattamento |
| --- | --- |
| `029e6d42e` | Checkpoint senza modifiche ai file |
| `743863cd4` | Ricerca interattiva di serie complete dalla barra della serie |
| `aebe5ffea` | Range multi-stagione, tracking e importazione per file; combinati con gaboy |
| `91612f21f`, `904df6a6a` | Identità della serie verificata e conservata nel matching delle release complete |
| `7d97ad267` | Comando `CompleteSeriesSearch` circoscritto alla serie, con modalità `dryRun` |
| `1dabf5ec9` | Progresso della coda attribuito agli episodi della stagione effettiva |
| `fbe07a25d` | Priorità: pacchetti multi-stagione, stagione singola, episodi |
| `eb8234aa2` | Prevenzione delle acquisizioni automatiche sovrapposte ai pacchetti attivi |
| `d7cb2a7c8` | Ricerca dei pacchetti anime prima degli episodi non coperti |
| `07dd5364c` | Search Monitored salta le stagioni complete |
| `b9412c01f` | Recupero degli episodi della coda in batch, evitando URL troppo lunghi |
| `f7d61012c` | Ricerca di serie completa quando più stagioni monitorate sono incomplete |
| `d5ed950e5` | RSS evita upgrade automatici delle stagioni complete |
| `241936c82` | Completamento dei download anche quando si conservano episodi esistenti preferiti |
| `f653d1c87` | Verifica delle numerazioni scene e libreria prima di acquisizione/importazione |
| `20cfbf14a`, `67f9cd3f3` | Copertura anime multi-stagione, tracking del grab e preprocessing parser |
| `61b4fde3f` | Recupero del tracking completo tramite cronologia di grab verificata |
| `6cce9e1b5` | Tag container immutabili per commit e build dei pull request |
| `3dd798386`, `91d3c8b31` | SDK coerente con `global.json`: mantenuto `10.0.401`, documentato |
| `8fd91541e` | Trigger manuali e permessi di workflow coerenti con il repository corrente |
| `751715c9d` | Query mirate ai pacchetti completi per gli indexer compatibili |
| `23efa34d0` | Test nel Docker build; conservata anche la CI separata già disponibile |
| `0464b0b6c` | `UMASK` configurabile, default `002`, compatibilità con `UMASK_SET` |
| `501315f2f` | Ricerca completa e stato episodi aggiornato nei batch di episodi mancanti |
| `d2e06dc05` | Speciali non monitorati quando un client aggiunge una serie senza scelta esplicita |

## Comportamenti da conoscere

La copertura di una serie completa è dedotta dal nome della release, non dalla
verifica di tutti i suoi file. Gli speciali sono facoltativi; per le serie in
corso si considerano tutte le stagioni regolari attualmente note.

La ricerca automatica opera sulle serie monitorate e sugli indexer torrent
abilitati alla ricerca automatica. La modalità `dryRun` mostra approvazioni e
rifiuti senza acquisire download né accodare release in attesa. Non crea ricerche
ricorrenti. Le ricerche manuali mantengono le normali regole di upgrade.

Tra le release approvate, la priorità dei pacchetti precede qualità e punteggio
dei custom format. I controlli dei profili e gli altri criteri di accettazione
restano necessari. RSS e Search Monitored saltano le stagioni già complete.

Sono mantenuti identità e liste AniList, override manuali, stagioni richieste,
riuso qBittorrent, sorgenti multi-stagione conservate e progresso gaboy. Il blocco
dei servizi cloud Sonarr segue ancora la policy del fork; nessun endpoint
condiviso viene riabilitato.

La cache delle ricerche complete è separata per ID locale di serie, anche quando
un indexer restituisce lo stesso GUID per ricerche diverse. I grab V3/V5
verificano il contesto originale. Per serie native con TVDB sintetico negativo,
un mapping scene esterno non modifica fonte, stagione o episodi; la ricerca
automatica completa rifiuta identificatori TVDB dichiarati non verificabili e
identificatori TVRage/IMDb contrari alla fonte. Il titolo non annulla questi
rifiuti.

Gli override manuali salvati mantengono gli ID scelti, con verifica che
appartengano al grab. La risoluzione locale dei file eredita il mapping della
propria stagione, non quello della prima stagione del pack. Il recupero della
cronologia non aggiunge episodi che non erano stati acquisiti.

## Uso e manutenzione

Dalla barra della pagina di una serie, aprire **Ricerca serie completa** per
esaminare le release prima di scegliere un download. Le API V3 e V5 accettano
`GET /api/v5/release?seriesId=ID&completeSeries=true`; non combinare questo
parametro con `episodeId` o `seasonNumber`.

Il comando automatico si avvia tramite `POST /api/v5/command`, con il corpo:

```json
{"name":"CompleteSeriesSearch","seriesId":42,"dryRun":true}
```

Sostituire `42` con l'ID locale della serie, non con l'ID AniList o TVDB. Il
risultato restituisce un comando da consultare attraverso
`GET /api/v5/command/ID`; i log riportano ogni candidato e il riepilogo dei
rifiuti. Impostare `dryRun` a `false` per consentire la normale acquisizione.
Il comando non configura un'attività periodica.

La toolchain è fissata da `global.json` a .NET SDK `10.0.401`; frontend Vite con
Node 24 e Yarn 1.22. I workflow CI e Docker hanno `workflow_dispatch`: in GitHub,
**Actions → CI/Docker → Run workflow** esegue un ramo scelto. Il workflow Docker
verifica avvio e test; le pull request costruiscono senza pubblicare, mentre i
push/manuali producono tag `sha-<commit completo>` immutabili. L'immagine è
derivata dal repository corrente in GHCR, con permesso `packages: write` nel
job. Le regole del repository devono consentire l'esecuzione delle Actions e la
scrittura del token per pubblicare immagini.

Il Compose incluso costruisce `sonarr-weeb:local` con `docker compose build` e
`docker compose up -d`. `SONARR_PRO_UMASK` controlla `UMASK` e vale `002` per
default; il solo entrypoint accetta anche il parametro storico `UMASK_SET`.

## Verifica

Questi risultati descrivono l'integrazione iniziale. Build e deploy Docker sono
stati successivamente eseguiti e verificati nel
[report Docker](docker-validation-2026-10-12.md).

| Controllo | Esito |
| --- | --- |
| Build dell'intera soluzione, Debug | Passata; 0 errori, 0 avvisi |
| Core, esclusa categoria `IntegrationTest` | 6.102 superati, 17 saltati, 0 falliti |
| API V3/V5 e risorse | 56 superati, 0 falliti |
| HTTP | 57 superati, 0 falliti |
| Policy cloud Sonarr, offline | 12 superati, 0 falliti |
| Test frontend di coda e batching | 11 superati, 0 falliti |
| Lint, Stylelint, TypeScript e build Vite | Passati sull'intero frontend |
| Entrypoint shell, YAML e percorsi MSBuild dei test Docker | Verificati staticamente |
| Avvio Windows con SQLite nuova | Passato; migrazioni fino a 242 |
| Aggiunta reale tramite API AniList | Cowboy Bebop, fonte `aniList`, ID `1`, TVDB sintetico `-1`, 26 episodi |
| Ricerca completa V3/V5 con indexer Torznab simulato locale | Un pack associato alla serie AniList e ai 26 episodi; scope non valido restituisce HTTP 400 |
| Comando `CompleteSeriesSearch` in dry run | Completato: 1 candidato approvato, 0 download e 0 release in attesa |
| Aggiunta senza opzione monitor esplicita | Speciali non monitorati; stagione regolare monitorata |

Le prime esecuzioni Core hanno rilevato fixture con mock obsoleti o ID episodici
duplicati, più due controlli da rafforzare: identità dichiarata dalle release
native e lookup scene degli anime nativi. Sono stati corretti prima della
verifica finale, completata senza fallimenti. I test API esercitano controller e
cache reali per ricerche
complete e di stagione con lo stesso GUID. Gli override manuali sono verificati
con parser, risolutore e specifiche reali, mantenendo il limite degli episodi del
grab.

La verifica runtime usa dati e cartelle temporanee, un feed torrent sintetico
locale e nessun client di download. Sono stati verificati candidati e dry run,
senza scaricare o spostare media. Non è una prova su una libreria reale o con un
account AniList e liste private. Docker non è disponibile: immagini, permessi
Linux e workflow GitHub non sono stati eseguiti localmente. I test del worker di
compressione richiedono il modulo Linux `fcntl` e non sono eseguibili su questo
Windows; la sintassi Python è stata verificata e la suite resta prevista nella
CI Linux.

SDK .NET `10.0.401`, Node `24.19.0`, Yarn `1.22.22`. Log completi e risultati TRX
in `%LOCALAPPDATA%\SonarrWeebTools`; esito dell'avvio in
`ikatun-smoke-result.json`. Nessun repository o container è stato pubblicato.
