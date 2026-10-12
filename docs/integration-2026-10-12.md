# Integrazione Sonarr Pro del 12 ottobre 2026

Il ramo locale `integration/sonarr-latest-anilist` contiene le funzionalità di
Sonarr Pro ribasate sull'ultimo commit del ramo predefinito Sonarr disponibile
durante questo lavoro, `v5-develop`. Questa base è di sviluppo.

L'integrazione successiva del fork ikatun è descritta nel
[report delle funzionalità e verifiche ikatun](ikatun-integration-2026-10-12.md).
Le verifiche elencate qui si riferiscono alla prima integrazione Kakarotto/gaboy.

## Sorgenti

| Repository / ramo | Commit |
| --- | --- |
| [KakarottoCake/Sonarr-Pro, main](https://github.com/KakarottoCake/Sonarr-Pro) | `c104bf96ffd2ebaa18999db29196160e4a7210b0` |
| [Sonarr/Sonarr, v5-develop](https://github.com/Sonarr/Sonarr/commit/698bb1dbb7cafc6ff3a5d1d15f3535a1d16da636) | `698bb1dbb7cafc6ff3a5d1d15f3535a1d16da636` |
| [gaboy498-byte/Sonarr-Pro, main](https://github.com/gaboy498-byte/Sonarr-Pro) | `cef1369f86b23910c606c76e0b20d2ed86809ac7` |
| [gaboy, investigate/multiseason-and-fakefilter](https://github.com/gaboy498-byte/Sonarr-Pro/tree/investigate/multiseason-and-fakefilter) | `14eaaebf6267791c60c50a01b6d3109e18ccb53e` |

Il `main` di gaboy contiene una storia precedente delle funzionalità già presenti
in Kakarotto. Le aggiunte da integrare erano sul ramo di investigazione.

La storia Kakarotto originale è conservata nel ramo `source/kakarotto-main`.
Dato che il fork aveva riscritto la storia upstream, le modifiche fino al merge
`220d53447` sono state consolidate su `da990630a`; i successivi 14 commit sono
stati riprodotti e questa sequenza è stata ribasata su `698bb1dbb`.

## Compatibilità e AniList

- Conservate le funzioni Pro, la lingua dei metadati e gli ordinamenti Sonarr.
- Le migrazioni Pro 233–240 mantengono il loro numero. La nuova migrazione
  upstream 236 per `TagExisting` è rinumerata 241 per evitare una collisione.
- `/api/v5/settings/metadata` gestisce lingua e chiave TMDB mascherata;
  `/api/v5/settings/metadatasource` rimane disponibile come alias.
- Gli slug dei provider hanno prefissi `anilist-`, `mal-`, `tmdb-`, evitando
  collisioni fra ID numerici di cataloghi diversi.
- Le liste AniList importano le voci dell'utente indicato. Lo stato personale
  (Watching, Planning, Completed, Dropped, Paused, Repeating), lo stato di
  pubblicazione dell'anime e l'appartenenza alla lista personalizzata sono
  filtri distinti e combinati. Un valore `false` in `customLists` non indica
  appartenenza. Non sono liste di scoperta, popolarità o stagione del catalogo.
- Le voci native AniList conservano provider e ID: sequel, OVA e recut non
  vengono convertiti automaticamente nella serie padre TVDB. Anteprima,
  sincronizzazione, deduplicazione e pulizia usano identità dello stesso catalogo.
- Errori GraphQL, paginazione incompleta e risposte non valide sono segnalati
  come fallimenti; una lista parziale non autorizza pulizie della libreria.
- Le nuove esclusioni conservano l'identità del provider. Le esclusioni native
  precedenti che conservavano soltanto un ID TVDB negativo dopo l'eliminazione
  non possono essere ricostruite automaticamente.
- I dati privati AniList richiedono autenticazione dello stesso utente della
  lista. L'accesso pubblico non permette di leggere liste private altrui.

Riferimenti API: [liste utente](https://docs.anilist.co/guide/graphql/queries/media-list),
[paginazione](https://docs.anilist.co/guide/graphql/pagination),
[errori](https://docs.anilist.co/guide/graphql/errors),
[autenticazione](https://docs.anilist.co/guide/auth/).

## Servizi cloud condivisi disabilitati

La policy del fork blocca le richieste a `services.sonarr.tv` e
`sentry.sonarr.tv`, compresi i redirect HTTP. Le factory restano disponibili
per l'iniezione delle dipendenze; il blocco avviene quando viene costruita o
inviata una richiesta, senza includere token o query string nell'errore.

Gli aggiornamenti cloud dei mapping scene e dell'elenco delle serie giornaliere
e l'OAuth MyAnimeList tramite i servizi Sonarr sono quindi disabilitati. I
mapping scene già presenti nel database vengono conservati; la cache delle
serie giornaliere è soltanto in memoria e può scadere. SkyHook TVDB e i provider
AniList, TMDB e MyAnimeList/Jikan rimangono disponibili sulle rispettive API.

## Funzionalità gaboy

Integrate e adattate le modifiche dei primi sei commit del ramo, da `9cd27f4`
a `79ef6d2`: range espliciti multi-stagione, scene mapping per stagione,
importazione e riconciliazione dei vecchi grab, progresso per episodio e
controlli sui titoli e sulle rotte di invio dei torrent. Sono conservati gli
override manuali Pro, le stagioni richieste, il riuso qBittorrent e la gestione
delle sorgenti multi-stagione.

Il recupero dei metadati magnet dei commit `872d7cf` e `14eaaeb` è escluso:
richiede `MonoTorrent 3.0.2-sonarrsecurity.1.rev0002`, assente dal feed NuGet
pubblico. La versione ufficiale non espone i limiti di memoria richiesti da
quel codice. I magnet senza metadati restano quindi un limite dell'ispezione.

## Verifica

| Controllo | Esito |
| --- | --- |
| `dotnet build src/Sonarr.sln -c Debug --no-restore` | Passato; 0 errori, 0 avvisi |
| Core, esclusa categoria `IntegrationTest` | 5.805 superati, 17 saltati, 0 falliti |
| API | 39 superati, 0 falliti |
| HTTP | 57 superati, 0 falliti |
| Policy HTTP dei servizi cloud, test offline | 12 superati, 0 falliti |
| `yarn typecheck`, `yarn build` | Passati |
| `yarn lint`, `yarn stylelint -f github` | Passati sull'intero frontend |
| Avvio su Windows con database SQLite nuovo | Passato; migrazioni fino a 242 |
| API impostazioni | Lingua, chiave TMDB mascherata e alias storico verificati |
| Ricerca reale AniList via API locale | Cowboy Bebop, `aniList:1`, slug e ordinamento corretti |

La prima esecuzione ha rilevato regressioni del rebase e aspettative obsolete sui
log di autenticazione upstream. Dopo le correzioni, le suite Core e HTTP sono
state rieseguite e passano. L'autenticazione mantiene il comportamento upstream.
Dopo il blocco dei servizi cloud sono passati anche 285 test Core relativi a
liste, metadati e mapping; avvio, API coda e ricerca AniList sono stati verificati
di nuovo. Il totale delle verifiche distinte Core, API, HTTP e policy è 5.913
test superati, oltre ai 17 test Core saltati.

SDK .NET `10.0.401`, Node `24.19.0`, Yarn `1.22.22`. Gli strumenti locali e i
log completi sono in `%LOCALAPPDATA%\SonarrWeebTools`; i risultati TRX sono nella
sottocartella `test-results`. Git portatile è in `git\cmd\git.exe`.

Non è stata utilizzata una libreria reale: download, spostamenti di media,
autenticazione AniList con un account e dati privati non sono stati collaudati
dal vivo. I test `IntegrationTest` che contattano servizi esterni sono esclusi.
Non è stato pubblicato un repository o un'immagine Docker.

## Avvio della versione integrata

Il file `docker-compose.yml` costruisce l'immagine locale `sonarr-weeb:local`
da questi sorgenti. Dopo aver configurato i volumi e gli ID utente:

```sh
docker compose build
docker compose up -d
```

L'immagine pubblica Kakarotto indicata nel README originale non contiene queste
modifiche locali. Docker non è disponibile nell'ambiente di verifica.
