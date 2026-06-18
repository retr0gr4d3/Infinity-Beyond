using HarmonyLib;
using Infinity_TestMod.Util;
using UnityEngine;

namespace Infinity_TestMod.Patches
{
    // Passive music catalog feeder. Fires for every track the game registers
    // with BGMusicManager — area BGM, cutscene stings, our own Jukebox loads.
    // Postfix so the track is fully added before we record.
    [HarmonyPatch(typeof(BGMusicManager), nameof(BGMusicManager.AddTrack))]
    public static class MusicHarvestPatch
    {
        public static void Postfix(int id, AudioClip clip, string name)
        {
            try
            {
                float len = clip != null ? clip.length : 0f;
                MusicCatalog.Record(id, name ?? "", len, name ?? "");
            }
            catch { }
        }
    }
}
