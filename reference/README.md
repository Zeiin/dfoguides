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


`titleicons/<title>.png`: icons made by hand for titles that no image source has (drop a PNG named after the title, without its Platinum level; characters a file name cannot hold become `-`). `export-site` copies them into `stats/img/items` and uses them for every level of that title.

`oathicons/<oath item name>.png`: hand-made icons of the oath items (the image server has none), cropped from in-game screenshots (46x46, frame removed). A file named `<item name> [<Rarity>].png` is used for that rarity only (`Eternal Mist Oath` exists in several rarities with different colours); `<item name>.png` serves every rarity. `export-site` copies them into `stats/img/items` as `oath-<slug>.png`. Drop a new PNG here to cover a new oath.
