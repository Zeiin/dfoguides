# Collector

The .NET console app behind the statistics page. Build with `dotnet build tools/Collector/Collector.csproj -c Release` and run `dotnet tools/Collector/bin/Release/net10.0/Collector.dll <command>` from anywhere inside the repository. With no command it prints the usage list.

Output goes to `<repo>/data/` (gitignored): `raw/` is what the API returned, `out/` is analysis output. The website files are written to `<repo>/stats/`.

## Commands

Commands that call the API need `NEOPLE_API_KEY`.

| Command | Purpose |
|---|---|
| `jobs` | Save the class list (`data/raw/jobs.json`). Run first. |
| `refresh [delayMs] [budgetMinutes] [keys]` | The daily job: rescan every class roster, pull new and changed characters plus the oldest 1/7 of the rest, keep everything else from last time. Resumable. Stops cleanly when `budgetMinutes` is used up or the API pushes back (5 rate-limit responses). `keys` limits it to some classes. Writes `data/out/refresh_summary.txt` and `data/raw/refresh_summary.txt` (coverage, whether the run was limited to some classes, counts). The fame search has no upper limit. |
| `scan`, `pull-all` | The older full pipeline: roster everything, then download everything. `refresh` replaces it for normal use. |
| `analyze-every [minFame]` | Statistics for every class and for all/DPS/Buffers (default cutoff 91582). |
| `export-site [skip-icons]` | Write `stats/data/` and the item icons. Run `analyze-every` first. |
| `fetch-skill-icons`, `fetch-skill-details` | Save the skill lists and prerequisites into `reference/` and download skill icons. |
| `fetch-skill-layout`, `fetch-set-icons` | Match the in-game skill grid and the gear set emblems from dfmax.xyz pages. |
| `apply-skill-icon-map` | Apply `reference/skilliconmap.json` (the Dark Knight and Creator icons). Run after `fetch-skill-layout`. |
| `sample`, `sample-all`, `probe`, `character`, `get`, `damage-types`, `fetch-art`, `ratetest` | Exploration helpers. `ratetest` sends many requests quickly and must only be run on purpose. |

## Rules the statistics follow

- Only characters at or above the fame cutoff (91,582), active within 90 days (the API's fame search).
- No oath: left out of the oath tables. 1 or 2 Black Fang pieces: left out of the Black Fang table. Non-Rare-Clone weapon avatars: left out of the weapon avatar table.
- A character whose current class differs from the class it was pulled for (class change coupon) is skipped.
- Incremental refresh: if a character's fame went down **and** it switched into Magic Domain (gear set) or Magic Verge (oath), its previous snapshot is kept (Magic Domain is worn for farming, not content). Characters that dropped out of the roster are probed once for the same reason and otherwise removed.

## Safety

- The key is read from the environment only; nothing prints request URLs, and there is no HTTP logging.
- The throttle defaults to 250 ms between calls. The daily workflow runs without a pause (`refresh 0`). The API's 429 responses are retried with backoff and five of them stop a run.
- Neople's terms: display attribution, no compensation from users for API access.

## Layout

- `Program.cs` finds the command by name; `Commands/` has one class per command.
- `NeopleClient.cs` (requests, throttle, retry), `Roster.cs` (fame search), `CharacterPuller.cs` (downloads), `Refresh.cs` (incremental rules).
- `CharacterRecord.cs` reads the saved files into one row per character; `Stats.cs` aggregates; `SiteExport.cs`, `SiteRecords.cs`, `SiteIcons.cs`, `SetEmblems.cs` write the website data.

The page-side code that mirrors `Stats.cs` for filtering is `stats/filter.js`; if one changes, change the other.
