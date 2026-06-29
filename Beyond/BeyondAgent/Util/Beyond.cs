using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace BeyondAgent.Util
{
    // Standalone runtime support for the Beyond agent. No third-party loader is
    // involved: the agent is injected by our own launcher (see
    // Launcher/AssemblyPatcher.cs) and ticked by BeyondLifecycle. These types
    // live in the root BeyondAgent namespace so every BeyondAgent.*
    // file resolves them via enclosing-namespace lookup, no using needed.

    // Static logging facade. Everything funnels into the Unity player log
    // (Player.log), which is where our standalone build's output goes.
    public static class BeyondLog
    {
        public static void Msg(string msg)
        {
            Debug.Log("[Beyond] " + msg);
        }

        public static void Warning(string msg)
        {
            Debug.LogWarning("[Beyond] " + msg);
        }

        public static void Error(string msg)
        {
            Debug.LogError("[Beyond] " + msg);
        }
    }

    // Coroutine pump. We have no loader-provided host, so we run coroutines on
    // the game's AEC singleton (a long-lived MonoBehaviour).
    public static class BeyondCoroutines
    {
        public static void Start(IEnumerator routine)
        {
            if (AEC.Instance != null)
            {
                AEC.Instance.StartCoroutine(routine);
            }
            else
            {
                Debug.LogError("[Beyond] Cannot start coroutine: AEC.Instance is null");
            }
        }
    }

    // Path helper for persisted data. Mirrors the game's UserData layout next
    // to the executable.
    public static class BeyondEnv
    {
        public static string UserDataDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData");
    }
}
