# DFO Guides

Source of [dfoguides.com](https://dfoguides.com): community-written guides for Dungeon Fighter Online (Global server), plus a class statistics page built from Neople Open API data.

Unofficial fan project, not affiliated with Neople or Nexon. Data provided by the Neople Open API.

## What is in here

| Path | What |
|---|---|
| `index.html`, `images/`, `CNAME` | Landing page (guides and statistics entries, side menu). |
| `legion/` | Hand-written guides (plain HTML). |
| `stats/` | The statistics page: plain HTML, CSS and JavaScript, no framework and no build step. |
| `stats/data/` | **Generated** (`data.js` and per-class `records/*.js`). Not in this branch: the workflow publishes it on the `data` branch. |
| `tools/Collector/` | C# (.NET 10) console app that pulls character data from the API and builds the statistics. See `tools/README.md`. |
| `reference/` | Hand-collected inputs that no job generates (skill lists, prerequisites, in-game skill layout, set families, icon sources). See `reference/README.md`. |
| `.github/workflows/` | `stats.yml` (daily data refresh) and `pages.yml` (deploy). |
| `data/` | Local working data (raw API dumps, analysis output). Gitignored. |

## How the statistics work

1. The Collector scans the fame rankings of every class (high-fame characters only, so the numbers describe top players) and downloads each character's equipment, oath, skills, avatar and creature.
2. It aggregates usage rates per class (gear sets, weapons, titles, oath sets and options, special pieces, skill picks, avatars, emblems).
3. `export-site` writes `stats/data/data.js` (the aggregates) and `stats/data/records/<class>.js` (compact anonymous character rows, no names or ids). The page uses the rows to recompute every number when you filter by set, oath set or fame range.
4. The static page in `stats/` renders it. Everything runs in the browser.

## Running it locally

Requirements: the [.NET 10 SDK](https://dotnet.microsoft.com/download) and, for anything that calls the API, a Neople Open API key.

```
git clone https://github.com/Zeiin/dfoguides
cd dfoguides
echo NEOPLE_API_KEY=your-key-here > .env      # .env is gitignored; or set NEOPLE_API_KEY in your environment
dotnet build tools/Collector/Collector.csproj -c Release
```

Run the whole pipeline (the first full pull takes hours; it is resumable):

```
dotnet tools/Collector/bin/Release/net10.0/Collector.dll jobs
dotnet tools/Collector/bin/Release/net10.0/Collector.dll refresh
dotnet tools/Collector/bin/Release/net10.0/Collector.dll analyze-every
dotnet tools/Collector/bin/Release/net10.0/Collector.dll export-site
```

View the site (any static server works):

```
python -m http.server 8000      # then open http://localhost:8000/
```

The API key is read from the `NEOPLE_API_KEY` environment variable or `.env`. It is never logged, and request URLs are never saved (the API takes the key as a query parameter).

## Publishing

- `pages.yml` deploys the site to GitHub Pages on every push to `main`. It combines `main` with the latest generated data from the `data` branch, deploys, and then checks that the landing page, `/stats/` and the data files answer.
- `stats.yml` runs daily (and on demand): it restores the cached character data, pulls only new and changed characters, rebuilds the statistics and replaces the `data` branch with a single commit (no history is kept). It needs the repository secret `NEOPLE_API_KEY`. It publishes only when enough characters have data, so a run that is cut short never replaces good data.
- A push that changes `tools/Collector/` or `reference/` rebuilds the data from the cache without calling the API.

## Credits

Gear, oath and skill data: Neople Open API. In-game skill grid positions and set emblems: matched against dfmax.xyz pages (the same Neople assets). Item icons: Neople image server. See `tools/README.md` for how each is used.
