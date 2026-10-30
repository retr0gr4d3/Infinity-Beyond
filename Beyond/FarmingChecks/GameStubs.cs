using System.Collections;
public class Inventory : IEnumerable<InventoryItem>
{
    public List<InventoryItem> Bag = []; public List<InventoryItem> Bank = [];
    public int getItemQuantity(int id) => Bag.Where(i => i.ID == id).Sum(i => i.Quantity);
    public IEnumerator<InventoryItem> GetEnumerator() => Bag.GetEnumerator(); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
public class InventoryItem { public int ID; public string Name = ""; public int Quantity; }
public class PlayerData { public Inventory playerInventory = new(); }
public static class Entity { public static PlayerData myPlayerData = new(); }
public class shopData { public List<ShopItem> items = []; }
public class ShopItem { public int ID, TokID, TokQty, Cost; public bool Coins; public string Name = "", TokName = ""; public List<mergeItemTurnin> turnins = []; }
public class mergeItemTurnin { public int ItemID, Quantity; public string Name = ""; public string getName() => Name; public int getQuantity() => Quantity; }
namespace BeyondAgent.Util
{
    public static class BeyondEnv { public static string UserDataDirectory => Path.GetTempPath(); }
    public static class BeyondLog { public static void Msg(string s) { } public static void Warning(string s) { } public static void Error(string s) { } }
}
