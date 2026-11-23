namespace BeyondAgent.Util
{
    /// <summary>
    /// Loads + plays an arbitrary soundtrack by ID. Mirrors what
    /// Area.loadBGM does for the current map, but driven on demand:
    ///   1. If Music already has the track cached, just play it.
    ///   2. Otherwise Music.Load spins up a SoundtrackLoader → fetches
    ///      data/getsoundtracks?ids=&lt;id&gt; → downloads the bundle →
    ///      registers the track → we play it.
    /// </summary>
    public static class Jukebox
    {
        public static void Play(int id)
        {
            if (id <= 0)
            {
                BeyondLog.Warning($"[Jukebox] invalid id {id}");
                return;
            }
            Music music = Music.Instance;
            if (music == null)
            {
                BeyondLog.Warning("[Jukebox] no Music instance (no map loaded?)");
                return;
            }

            // Fast path — already loaded this track at some point.
            if (music.Tracks.TryGetValue(id, out Music.Track cached) && cached != null && cached.IsReady)
            {
                music.PlayTrack(id, withDelay: false);
                BeyondLog.Msg($"[Jukebox] playing cached track {id} ({music.GetTrackName(id)})");
                return;
            }

            try
            {
                music.Load(id, id,
                    track =>
                    {
                        try
                        {
                            Music.Instance?.PlayTrack(id, withDelay: false);
                            BeyondLog.Msg($"[Jukebox] loaded + playing track {id} ({track?.name})");
                        }
                        catch (System.Exception ex)
                        {
                            BeyondLog.Error($"[Jukebox] play failed: {ex.Message}");
                        }
                    },
                    err => BeyondLog.Warning($"[Jukebox] load failed: {err}"));
                BeyondLog.Msg($"[Jukebox] fetching track {id}…");
            }
            catch (System.Exception ex)
            {
                BeyondLog.Error($"[Jukebox] {ex.Message}");
            }
        }

        public static void Stop()
        {
            try { Music.Stop(); }
            catch (System.Exception ex) { BeyondLog.Error($"[Jukebox] stop: {ex.Message}"); }
        }

        public static void RestoreAreaBGM()
        {
            try { Area.currentArea?.EnableBGM(); }
            catch (System.Exception ex) { BeyondLog.Error($"[Jukebox] restore: {ex.Message}"); }
        }
    }
}
