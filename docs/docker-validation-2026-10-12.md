# Build e deploy Docker verificati

L'immagine `sonarr-weeb:local` è stata costruita realmente dal repository ed
eseguita come container Linux `amd64`. Il deploy Compose `sonarr-pro` è attivo
su [http://localhost:6970](http://localhost:6970), raggiungibile anche da Windows.
Configurazione e media locali sono in `config/` e `media/`, inizialmente vuoti.
Non sono stati usati una libreria reale o client di download.

## Immagine e ambiente

- Ramo: `integration/sonarr-latest-anilist`; integrazione funzionale di partenza
  `e12e717e2`, con correzioni di packaging e strumenti di deploy di questa verifica.
- ID dell'immagine: `sha256:570b34e50e28ee6c65336387ac4467b4f32764553fe467055f97d4959b8ead6f`.
- Versione runtime: `10.0.0.30175`; migrazione SQLite `242`.
- .NET SDK `10.0.401`, frontend Node 24/Vite, Docker Engine `29.9.0`, Compose `v5.6.0`.
- Docker è installato nella distribuzione WSL 2 dedicata `SonarrWeebBuild`, Ubuntu
  `24.04.5`. Docker Desktop non è stato installato.
- Archivio esportato: `_artifacts/sonarr-weeb-local-linux-amd64.tar`, 127.636.992 byte.
- SHA-256 dell'archivio: `08f7676f65cc544c97a60bd17a84d7fd5e898c5609d4bb01ac7a325bea3bf938`.

Per importare l'immagine su un altro host Linux `amd64`:

```sh
docker load -i sonarr-weeb-local-linux-amd64.tar
```

## Verifiche effettuate

| Controllo | Esito |
| --- | --- |
| `docker compose --progress plain build sonarr-pro` | Passato, frontend e backend Release/Posix pubblicati |
| Core dentro il Docker build | 6.096 superati, 27 saltati, 0 falliti |
| API dentro il Docker build | 56 superati, 0 falliti |
| HTTP dentro il Docker build | 57 superati, 0 falliti |
| Policy cloud Sonarr dentro il Docker build | 12 superati, 0 falliti |
| Test frontend dentro il Docker build | 11 superati, 0 falliti |
| Lint, Stylelint, typecheck e Vite dentro il Docker build | Passati |
| Worker di compressione, Python su Linux | 25 superati, 1 saltato; nessuna conversione di media reale |
| Container isolato, `/ping`, HTML e JavaScript compilato | Passati |
| API V5, SQLite nuova, serie e coda inizialmente vuote | Passati |
| Eseguibile nativo `/app/ffprobe`, utente 1000 | Passato |
| UID/GID 1000, umask `002`, volumi scrivibili | Passati; file `664`, directory `775` su filesystem Linux |
| Chiave TMDB sintetica mascherata, alias API e lingua su save legacy | Passati usando esclusivamente dati temporanei |
| Ricerca AniList reale dal container | Identità AniList conservata, nessuna serie aggiunta |
| Restart del container, database, impostazioni e media persistenti | Passati |
| Deploy con il Compose incluso, accesso da Windows | Passato su `127.0.0.1:6970` |
| Riavvio della distribuzione WSL e del servizio Docker | Immagine conservata e container ripartito automaticamente |
| Disponibilità dopo inattività, senza nuovi comandi WSL | Verificata da Windows; stessa istanza e stesso orario di avvio |

I dati del test runtime e i relativi container sono stati rimossi; il deploy
Compose rimane attivo con dati nuovi, senza chiavi TMDB sintetiche salvate. I test
su filesystem Linux verificano i permessi POSIX; i bind mount sul disco Windows
seguono anche le ACL del filesystem host.

## Correzioni necessarie

Il pacchetto NuGet distribuisce `ffprobe` Linux senza bit di esecuzione. La build
Docker e la CI ora lo rendono eseguibile prima dei test. Le istruzioni frontend
sono aggiornate a Vite e l'avvio usa l'immagine locale integrata.

Compose usa directory locali concrete al posto di `/path/to/media`, espone la
porta soltanto su loopback per default e permette di scegliere percorsi, UID/GID,
timezone e umask tramite variabili `SONARR_PRO_*`. Configurazione, media, `.env`
e cache Python sono esclusi da Git e dal contesto Docker.

Lo smoke test risolve nuovamente la porta dinamica dopo un restart: Docker può
assegnarne un'altra. Il workflow Docker usa ora questo test completo, invece del
solo `/ping`; la ricerca AniList esterna rimane facoltativa per la CI.

Nell'ambiente WSL è stato allineato il percorso persistente di containerd tra
l'avvio iniziale e il servizio systemd. Le immagini e il deploy sopravvivono al
riavvio. I servizi systemd da soli non mantengono attiva una distribuzione WSL:
il launcher apre una sessione nascosta per mantenere disponibile il servizio.
Questo comportamento è descritto nella
[documentazione Microsoft](https://learn.microsoft.com/en-us/windows/wsl/systemd/).

## Comandi di gestione

Su questo Windows, dalla cartella del repository:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File docker/start-wsl.ps1
```

Il launcher riusa la sessione WSL già attiva. Aggiungere `-Build` ricostruisce
l'immagine prima dell'avvio. Nessun avvio automatico al login Windows è stato
configurato: dopo un reboot del PC, eseguire il launcher.

Su un host che dispone già del comando Docker:

```sh
mkdir -p media
docker compose up -d --build
python3 docker/tests/smoke.py --image sonarr-weeb:local --anilist
```

Per arrestare il deploy locale:

```powershell
wsl.exe -d SonarrWeebBuild --cd /mnt/c/Users/tirael/Documents/SonarrWeeb --exec docker compose stop
```

Il volume `/media` è ancora vuoto. Per una libreria reale, impostare
`SONARR_PRO_MEDIA_PATH` al percorso desiderato e configurare root folder, client
e categorie nell'applicazione. Download, importazioni di media, liste private e
architettura ARM64 non sono stati collaudati in questo deploy.

Log e report completi sono in `%LOCALAPPDATA%\SonarrWeebTools`:
`docker-build-final.log`, `docker-smoke-report-final.json`,
`docker-deployment-report.json` e `compression-linux-tests.log`.
Nessuna immagine è stata pubblicata su un registry.
