# reference

Inputs the website export needs that no scheduled job generates. They are committed so a fresh clone (or the workflow) can run `export-site`.

| File | What | Regenerate with |
|---|---|---|
| `skilllists/skilllist_<class>.json` | Every skill of each class (id, name, required level, type). | `fetch-skill-icons` (API) |
| `skillprereqs.json` | Skills that must be learned before another (`<jobId>_<skillId>` to ids). | `fetch-skill-details` (API, slow) |
| `skilllayout.json` | Each skill's column and row in the game's skill window. | `fetch-skill-layout` (dfmax.xyz), then `apply-skill-icon-map` |
| `setfamilies.json` | Gear set page name to set family. | `fetch-set-icons` (dfmax.xyz) |
| `skilliconmap.json`, `skillicon-sources/` | Hand-made mapping of Dark Knight and Creator skills to icons (neither the API nor dfogang has these icons). | By hand; apply with `apply-skill-icon-map` |

Class portraits and item icons are committed under `stats/img/`.
