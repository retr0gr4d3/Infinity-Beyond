using BeyondAgent.Util;
using HarmonyLib;
using UnityEngine;

namespace BeyondAgent.Patches
{
    // Passive music catalog feeder. Fires for every track the game registers
    // with Music (area BGM, cutscene stings, our own Jukebox loads). Postfix
    // so the track is fully added before we record. Music.AddTrack is private,
    // hence the name string.
    [HarmonyPatch(typeof(Music), "AddTrack")]
    public static class MusicHarvestPatch
    {
        public static void Postfix(AudioClip clip, string name, int soundtrackID)
        {
            try
            {
                float len = (clip?.length) ?? 0f;
                MusicCatalog.Record(soundtrackID, name ?? "", len, name ?? "");
            }
            catch { }
        }
    }
}
