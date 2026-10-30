using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Launcher;

public static class BotScriptFile
{
    public static async Task SaveAsync(string path, string json)
    {
        string destination = Path.GetFullPath(path);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
