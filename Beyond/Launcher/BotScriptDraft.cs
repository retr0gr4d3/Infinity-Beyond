using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Launcher;

public sealed class BotStepDraft
{
    public JObject? OriginalEntry { get; set; }
    public JObject? OriginalOption { get; set; }
    public string Label { get; set; } = "";
    public int QuestId { get; set; }
    public string Monster { get; set; } = "";
    public string Area { get; set; } = "";
    public string Room { get; set; } = "";
    public string Item { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public override string ToString() => string.IsNullOrWhiteSpace(Label)
        ? QuestId > 0 ? $"Quest {QuestId}" : $"{Monster}: {Item}" : Label;
}

public sealed class BotScriptDraft
{
    public const string QuestChain = "quest-chain", QuestRewards = "quest-rewards", MergeShop = "merge-shop", MonsterDrops = "monster-drops";
    public JObject? OriginalForm { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Template { get; set; } = QuestChain;
    public int ShopId { get; set; }
    public string ShopMap { get; set; } = "";
    public List<BotStepDraft> Steps { get; set; } = [];

    public static BotScriptDraft FromJson(JObject document, string? activity = null)
    {
        var prop = activity == null ? document.Properties().FirstOrDefault(p => !p.Name.StartsWith('_') && (p.Value is JObject || p.Value is JArray)) : document.Property(activity);
        if (prop == null) throw new ArgumentException("No activity found in this script.");
        var form = prop.Value as JObject ?? new JObject { ["entries"] = prop.Value.DeepClone() };
        string type = (string?)form["template"] ?? QuestChain;
        if (type != QuestChain && type != QuestRewards && type != MergeShop && type != MonsterDrops) throw new ArgumentException("This script type is not supported by the writer yet.");
        var draft = new BotScriptDraft { Name = prop.Name, Description = (string?)form["description"] ?? "", Template = type, OriginalForm = (JObject)form.DeepClone(), ShopId = (int?)form["shop"]?["shop"] ?? 0, ShopMap = (string?)form["shop"]?["area"] ?? "" };
        var source = form[type is QuestRewards or MonsterDrops ? "options" : type == MergeShop ? "farm" : "entries"] as JArray;
        if (source == null) throw new ArgumentException("This legacy layout cannot be edited in the writer yet. The file has not been changed.");
        foreach (JToken token in source)
        {
            if (token is not JObject obj) throw new ArgumentException("Invalid script step.");
            JObject entry = obj;
            if (type is QuestRewards or MonsterDrops)
            {
                if (obj["farm"] is not JArray farm || farm.Count != 1 || farm[0] is not JObject step) throw new ArgumentException("The writer currently supports one step per selectable reward. The file has not been changed.");
                entry = step;
            }
            var hunt = entry["hunt"];
            var goal = entry["until"] as JObject;
            draft.Steps.Add(new BotStepDraft
            {
                Label = type is QuestRewards or MonsterDrops ? (string?)obj["name"] ?? "" : "",
                QuestId = (int?)entry["qid"] ?? 0,
                Monster = hunt is JArray monsters ? string.Join(", ", monsters.Select(m => (string?)m)) : (string?)hunt ?? "",
                Area = (string?)entry["area"] ?? "",
                Room = (string?)entry["frame"] ?? "",
                Item = (string?)entry["item"] ?? (string?)goal?["item"] ?? "",
                Quantity = (int?)entry["qty"] ?? 1,
                OriginalEntry = (JObject)entry.DeepClone(),
                OriginalOption = type is QuestRewards or MonsterDrops ? (JObject)obj.DeepClone() : null
            });
        }
        return draft;
    }

    public JObject ToJson()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().StartsWith('_')) throw new ArgumentException("Give your bot a name that does not start with an underscore.");
        if (Steps.Count == 0 && Template != MergeShop) throw new ArgumentException("Add at least one step.");
        if (Template != QuestChain && Template != QuestRewards && Template != MergeShop && Template != MonsterDrops) throw new ArgumentException("Choose a bot type.");
        if (Template == MergeShop && ShopId <= 0) throw new ArgumentException("Enter the merge shop ID.");
        var form = OriginalForm == null ? new JObject() : (JObject)OriginalForm.DeepClone();
        form["template"] = Template; form["description"] = Description.Trim();
        var steps = new JArray();
        foreach (var step in Steps)
        {
            bool quest = Template is QuestChain or QuestRewards || Template == MergeShop && step.QuestId > 0;
            if (quest && step.QuestId <= 0) throw new ArgumentException($"{step}: choose a quest from the search results (or enter its ID under Advanced).");
            if (!quest && string.IsNullOrWhiteSpace(step.Monster)) throw new ArgumentException($"{step}: choose a monster name or ID.");
            if ((Template is MergeShop or MonsterDrops) && string.IsNullOrWhiteSpace(step.Item)) throw new ArgumentException($"{step}: enter the material or drop name/ID.");
            if (step.Quantity < 1) throw new ArgumentException("Desired totals must be positive whole numbers.");
            var entry = step.OriginalEntry == null ? new JObject() : (JObject)step.OriginalEntry.DeepClone();
            entry["area"] = step.Area.Trim(); entry["frame"] = step.Room.Trim();
            if (quest)
            {
                entry.Remove("hunt");
                entry["qid"] = step.QuestId;
            }
            else
            {
                entry.Remove("qid");
                entry.Remove("until");
                var monsters = step.Monster.Split(',').Select(m => m.Trim()).Where(m => m.Length > 0).ToArray();
                if (monsters.Length == 0) throw new ArgumentException("Choose at least one monster.");
                entry["hunt"] = new JArray(monsters);
            }
            if (Template == MergeShop)
            {
                if (quest) { entry.Remove("item"); entry.Remove("qty"); var goal = entry["until"] as JObject ?? new JObject(); goal["item"] = step.Item.Trim(); entry["until"] = goal; }
                else entry["item"] = step.Item.Trim();
            }
            else if (Template == MonsterDrops) { entry["item"] = step.Item.Trim(); entry["qty"] = step.Quantity; }
            if (Template is QuestRewards or MonsterDrops)
            {
                var option = step.OriginalOption == null ? new JObject() : (JObject)step.OriginalOption.DeepClone();
                option["name"] = string.IsNullOrWhiteSpace(step.Label) ? step.ToString() : step.Label.Trim();
                option["farm"] = new JArray(entry); steps.Add(option);
            }
            else steps.Add(entry);
        }
        if (Template == MergeShop) { var shop = form["shop"] as JObject ?? new JObject(); shop["shop"] = ShopId; shop["area"] = ShopMap.Trim(); form["shop"] = shop; form["farm"] = steps; }
        else form[Template == QuestChain ? "entries" : "options"] = steps;
        return new JObject { [Name.Trim()] = form };
    }
}
