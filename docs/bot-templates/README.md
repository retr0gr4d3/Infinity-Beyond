# Reusable farming activities

Copy a template, rename its top-level activity, replace the example IDs, names and locations, then load the JSON in Beyond. The same templates work for later releases without changing the runner.

| Template | Choice | Behavior |
| --- | --- | --- |
| quest-chain | Start a quest line | Run entries in order, skipping completed story quests. |
| quest-rewards | Toggle independent quests | Run only selected quests once each. |
| merge-shop | Toggle live shop rewards and desired totals | Farm their combined material costs and purchase the selected rewards. |
| monster-drops | Toggle drops and desired inventory totals | Hunt for drops without accepting quests. |

The four template JSON files contain placeholders, not verified game content. They are packaged in FarmingTemplates, separate from runnable bots.

## Reward choices

`options` is an array of objects with `name`, optional `description`, and `farm` steps. Toggles start unchecked. Only selected plans are sent to the game. Single hunt options with a `qty` field offer an editable inventory total. Quest choices run one completion; supply an `until` item goal when its reward ID is known to skip already-owned rewards.

## Merge shops

`shop` is an object such as `{ "shop": 2797, "area": "mystcroft" }`. The launcher reads live shop rewards and prices. Quantity means desired bag totals. Already-owned rewards reduce material needs, and materials shared across selected rewards are added together. Banked items do not count. A preview shows material totals, and the runner recalculates on start.

`farm` supplies direct material routes. Hunts use `hunt` (monster names or IDs), `area`, `frame`, and `item` (material name or ID). Omit `qty` so its goal comes from the selected rewards. Repeatable quests use `qid` and `until: { "item": "Material name" }`. Irrelevant routes are skipped. Live discovery can supply missing routes; a material with neither a confirmed source nor an authored fallback stops the bot before hunting. This activity does not run story quests. If the game gates a shop or room, that access still needs unlocking; visit the shop map and use Reload shop if its preview does not load.

Only material/token-priced rewards are offered; gold/AC-only purchases are excluded. Recursive merging of intermediate rewards is not automated. Targets must fit inventory and stack limits.

## Monster drops

Hunts have no quest ID and use a positive `qty` inventory goal. Several drops from the same boss can share a route; all selected drops are retained throughout the run, so incidental progress toward another selected drop counts. The Bot panel has Farm monster drops: choose a monster on the current map, select its server-provided drops and set desired totals. Map, room and IDs are automatic; the Inspect window is not opened.

## Bundled content

Aranx Pets offers four independent quests. Mogloween Merge Rewards uses shop 2797 and material 104739 with the recorded Skeleton (534) drop route in Mystcroft R2. Mogloween Candy Corn farms that material alone with an editable total. These were recovered from local game records; live gameplay acceptance is pending.

Run `dotnet run --project Beyond/FarmingChecks -- --logic` for inventory/cost/parser checks. Use `--drops` for offline picker and automatic route checks. Run FarmingChecks without arguments for isolated popup previews. Its shop items are fixtures and it sends no purchase requests.


## Bot writer GUI

Open Bot writer in the Bot panel or Load Script window. Choose an activity type, name it, fill in a step and click Add / update step. Use New step, Remove, Up and Down to build the plan. Save script exports a runnable JSON file; Save and load selects that file in the loader without starting it. Open script loads an existing activity for editing; the loader's Edit Script opens it directly. Multi-activity files offer an activity picker, and saving retains other activities and extra settings. Advanced compound choices with multiple steps are currently refused to avoid losing data. Check saved plans with the normal Start flow. For merge routes, leave the quest ID blank to hunt, or enter a repeatable quest ID and its reward material. Monster and material IDs can be replaced by exact names. Material quantities are calculated at runtime from the player's reward choices. `--writer` runs template generation and parser checks.


The writer now displays numbered quest names. Select a plan step to read what the bot will do, search for quests by name, and use Apply changes to update the plan. Add another step opens an empty step form. Move steps with Up/Down. Map names are game travel names; a blank map means stay where you are. Quest IDs and starting room are under Advanced. Save file writes your work; Save & use in launcher also selects it for the separate Start Script action. Quest objectives/prerequisites are shown when available from local game data. Unknown quest names are labelled as unavailable rather than guessed.


## Live material-source discovery

Merge bots now discover sources from visible monster drop lists on the currently loaded shop map. Scripts can have an empty farm list: select a shop/map, choose rewards at run time, and let the runner build the material routes. Existing farm steps remain fallback routes. Discovery only queries monster types present in the loaded map; it does not claim a global monster-location directory. It never invents a source for an unreported item.

Requests are sequential and paced to at most one per second, with a five-second response timeout, up to 64 monster types and a 90-second overall search budget. Drop lists are cached per server and monster type for 15 minutes in UserData/Beyond/drop-sources.json, including confirmed empty lists. Map placements come from current game state. Candidate selection prefers alive monsters, then the current room, then reported expected drops per kill; it is not a measured fastest-kills estimate. Missing live sources use matching authored routes, otherwise the bot stops before farming. Set shop.autoDiscover=false to use only authored routes. Stop and pause remain available during discovery.

Checks: `dotnet run --project Beyond/FarmingChecks -- --sources` covers source matching, server isolation, expiry, cache persistence, pacing and timeouts.


## Bot folder

All runnable bots now live in the launcher's bots folder. The Library lists that folder, the writer saves there by default, and remembered FarmingPlans paths migrate automatically. Community bots are imported into it; unchanged imports can update, while local edits are preserved. The game's legacy chains.json overrides are imported before community copies so customized versions stay active.
