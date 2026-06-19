using BeyondAgent.Util;
using HarmonyLib;
using UnityEngine;

namespace BeyondAgent.Patches
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
                float len = (clip?.length) ?? 0f;
                MusicCatalog.Record(id, name ?? "", len, name ?? "");
            }
            catch { }
        }
    }
}
