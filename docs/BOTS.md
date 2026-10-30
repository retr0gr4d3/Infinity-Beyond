# Bots and authoring

The launcher keeps runnable JSON bots in `bots/` beside the executable. Library downloads are imported there. Unmodified imports can update; local edits are preserved. Legacy `FarmingPlans` contents and quest-chain overrides migrate automatically, with conflicting legacy folders retained in `repair-backups`. Templates are packaged separately in `FarmingTemplates/`.

## Run an activity

Start a game session and wait for the mod connection. Use **Search Scripts** to choose a bot, or **Load Script** to open a local file. **Start Script** opens any reward choices before running. **Stop Script** stops the runner; Pause/Resume retains its place.

| Activity | Player choice | Runtime behavior |
| --- | --- | --- |
| Quest chain | Start the quest line | Complete entries in order; skip completed story quests. |
| Quest rewards | Select independent reward quests | Run only selected quests, once each. |
| Merge shop | Select rewards and desired totals | Calculate combined costs, discover material sources, farm missing quantities, then buy rewards. |
| Monster drops | Select drops and desired totals | Hunt without accepting quests; count items already owned. |

**Farm monster drops** lists hostile monster types on the loaded map and requests their visible drop lists directly. It does not open Inspect. Monster and item IDs, map and room are supplied automatically. Hidden drops are excluded.

## Write or edit

Open **Bot writer**, choose an activity and add steps. Quest search uses readable names; each step has an explanation of its actions. Objectives and prerequisites appear when local quest data supplies them. Map names are game travel names. IDs and starting room remain under Advanced.

Select a step to edit it. **Apply changes** updates the plan; **Add another step** opens a new step. Reorder with Up/Down. **Save file** writes JSON; **Save & use in launcher** also loads it, and the separate Start action begins execution. **Open script** edits an existing file; multiple activities and additional metadata are retained. Unsupported compound choices are refused rather than truncated. Unsaved edits prompt before opening another file or closing.

See [template examples](bot-templates/README.md). New merge bots can omit manual material routes. Existing routes serve as fallbacks; setting `shop.autoDiscover` to false restricts farming to authored routes.

## Source discovery and limits

The runner queries visible drops for hostile types already known on the loaded shop map: one outstanding query, at most one per second, five seconds per response, at most 64 types, and a 90-second overall budget. Confirmed empty lists are cached too. Results are keyed by server and monster type with a 15-minute lifetime; placements come from current map state. Sources prefer alive monsters, then the current room, then reported expected drops per kill. This is not a measured kill-speed optimizer or a global source directory.

Materials shared by chosen rewards are combined. Bag counts include already-owned targets and exclude bank storage. Missing sources use an authored route or fail before farming. Purchase refusals, timeouts and missing costs fail visibly. Recursive intermediate merges, inventory-capacity planning and automatic bank withdrawals are not implemented. Room and shop access remain subject to game requirements.

The optional particle-speed compatibility patch retains normal spawning/position/lifetime behavior. If Unity speed bindings are unavailable, it warns once and keeps prefab particle speeds. Deployments synchronize existing game-root agent copies with Managed, retaining backups, so an old root assembly cannot shadow the current agent.

## Verification

From the repository root (with a configured game install for the agent build):

```powershell
dotnet build Beyond/BeyondAgent/BeyondAgent.csproj -c Release
dotnet build Beyond/Launcher/Launcher.csproj -c Release
dotnet run --project Beyond/FarmingChecks -c Release -- --all
dotnet run --project Beyond/ParticleChecks -c Release
```

FarmingChecks covers the actual parser, inventory/cost helpers, source discovery, deployment/migration and writer forms with offline fixtures. ParticleChecks exercises the real transpiler against simulated missing native bindings. Neither establishes live gameplay correctness. Run FarmingChecks without arguments for isolated popup previews; the shop list there is a fixture and makes no purchase requests.

Live acceptance should cover quest completion, pause/death recovery, material discovery/collection, selected purchases and visual effects. Current-map discovery and offline checks cannot establish complete official-server or content coverage. The custom `data/questdb` endpoint is optional InfinityServer functionality; it is not an AE API requirement.
