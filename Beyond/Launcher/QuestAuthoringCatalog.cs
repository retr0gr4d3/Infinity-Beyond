using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Launcher;

public sealed class QuestAuthoringCatalog
{
    public sealed record QuestChoice(int Id, string Name, string Area, string Room, string Objectives, int Previous)
    {
        public override string ToString() => Name;
    }
    private readonly Dictionary<int, QuestChoice> _quests = [];
    public QuestAuthoringCatalog(string? gameDirectory = null, IEnumerable<KeyValuePair<int, string>>? liveNames = null)
    {
        using var stream = typeof(QuestAuthoringCatalog).Assembly.GetManifestResourceStream("Launcher.QuestNames.json");
        if (stream != null) { using var reader = new StreamReader(stream); Read(JObject.Parse(reader.ReadToEnd())); }
        try
        {
            if (!string.IsNullOrWhiteSpace(gameDirectory))
            {
                string path = Path.Combine(gameDirectory, "UserData", "Beyond", "questdb.json");
                if (File.Exists(path)) Read((JObject)JObject.Parse(File.ReadAllText(path))["quests"]!);
            }
        }
        catch (Exception) { /* embedded names remain usable when local data is unavailable */ }
        foreach (var pair in liveNames ?? [])
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) continue;
            _quests.TryGetValue(pair.Key, out var old);
            _quests[pair.Key] = old == null ? new(pair.Key, pair.Value, "", "", "", 0) : old with { Name = pair.Value };
        }
    }
    private void Read(JObject entries)
    {
        foreach (var p in entries.Properties())
        {
            if (!int.TryParse(p.Name, out int id) || p.Value is not JObject q) continue;
            string name = (string?)q["name"] ?? ""; if (string.IsNullOrWhiteSpace(name)) continue;
            string objectives = string.Join("; ", (q["objectives"] as JArray ?? []).OfType<JObject>().Select(o => $"{(int?)o["qty"] ?? 1} x {o["name"]}"));
            _quests[id] = new(id, name, (string?)q["map"] ?? "", (string?)q["frame"] ?? "", objectives, (int?)q["prevQuest"] ?? 0);
        }
    }
    public QuestChoice? Get(int id) => _quests.TryGetValue(id, out var q) ? q : null;
    public string Name(int id) => Get(id)?.Name ?? $"Quest #{id} (name unavailable)";
    public QuestChoice[] All => _quests.Values.OrderBy(q => q.Name).ToArray();
    public string Describe(BotStepDraft step, string template)
    {
        string travel = string.IsNullOrWhiteSpace(step.Area) ? "Stay on your current map." : $"Travel to /{step.Area}" + (string.IsNullOrWhiteSpace(step.Room) ? "." : $", starting in room {step.Room}.");
        if (step.QuestId > 0)
        {
            var q = Get(step.QuestId);
            string text = $"{travel} Accept '{Name(step.QuestId)}', complete its objectives, then turn it in.";
            if (template == BotScriptDraft.QuestChain) text += " Skip this step if the quest is already completed.";
            if (template == BotScriptDraft.MergeShop) text += $" Repeat for {step.Item} until the selected shop rewards have enough materials.";
            if (!string.IsNullOrWhiteSpace(q?.Objectives)) text += "\nObjectives: " + q.Objectives;
            if (q?.Previous > 0) text += "\nPrerequisite: " + Name(q.Previous) + ".";
            return text;
        }
        return template == BotScriptDraft.MergeShop ? $"{travel} Defeat {step.Monster} for {step.Item}. The required amount comes from the rewards selected when starting this bot."
            : $"{travel} Defeat {step.Monster} until your inventory holds {step.Quantity} {step.Item}. Items already owned count toward this total.";
    }
}
