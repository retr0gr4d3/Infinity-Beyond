using UnityEngine;
using UnityEngine.InputSystem;

namespace BeyondAgent.Util
{
    public class BeyondLifecycle : MonoBehaviour
    {
        private static BeyondLifecycle _instance;

        public static void Create()
        {
            if (_instance != null)
            {
                return;
            }

            BeyondLog.Init();
            Application.logMessageReceivedThreaded += BeyondLog.WriteUnity;
            BeyondLog.Msg("Bootstrapping standalone agent...");

            // The Input System is focus-gated and we are never focused: the
            // launcher hosts the game window as a child of its own, so Windows
            // never marks it active. InputManager.ShouldFlushEventBuffer then
            // throws away the whole event buffer every update and nothing the
            // game reads through Keyboard.current ever fires - Enter-to-chat,
            // WASD, skill keys. Legacy Input/IMGUI reads WM_KEYDOWN directly,
            // which is why typing into an already-focused field still worked.
            // Both settings are needed: runInBackground on its own makes the
            // focus handler disable the devices instead of flushing.
            // Separate method so a game build without the Input System package
            // fails here instead of taking the whole bootstrap down with it.
            try { IgnoreWindowFocusForInput(); }
            catch (System.Exception ex) { BeyondLog.Error("Input focus fix failed: " + ex); }

            try { InputDiagnostics.Start(); }
            catch (System.Exception ex) { BeyondLog.Error("InputDiagnostics.Start failed: " + ex); }

            GameObject go = new("BeyondAgent");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<BeyondLifecycle>();

            // Initialize the TestMod
            BeyondAgentClass.Initialize();
        }

        private static void IgnoreWindowFocusForInput()
        {
            BeyondLog.Verbose($"Input focus fix: before runInBackground={Application.runInBackground} backgroundBehavior={InputSystem.settings.backgroundBehavior}");
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            BeyondLog.Msg($"Input focus fix: after runInBackground={Application.runInBackground} backgroundBehavior={InputSystem.settings.backgroundBehavior}");
        }

        private void Update()
        {
            // Before the agent's own tick: the game reads Keyboard.current in
            // its Update, so the sooner the state event is queued the better.
            try { KeyboardBridge.Tick(); } catch (System.Exception ex) { BeyondLog.Exception("KeyboardBridge.Tick", ex); }
            try { InputDiagnostics.Tick(); } catch (System.Exception ex) { BeyondLog.Exception("InputDiagnostics.Tick", ex); }

            if (BeyondAgentClass.activeInstance != null)
            {
                try { BeyondAgentClass.activeInstance.OnUpdate(); } catch (System.Exception ex) { BeyondLog.Exception("OnUpdate", ex); }
            }
        }

        private void OnGUI()
        {
            if (BeyondAgentClass.activeInstance != null)
            {
                try { BeyondAgentClass.activeInstance.OnGUI(); } catch (System.Exception ex) { BeyondLog.Exception("OnGUI", ex); }
            }
        }

        private void OnApplicationQuit()
        {
            if (BeyondAgentClass.activeInstance != null)
            {
                try { BeyondAgentClass.activeInstance.OnApplicationQuit(); } catch (System.Exception ex) { BeyondLog.Error("OnApplicationQuit threw: " + ex); }
            }
            BeyondLog.Msg("Application quitting");
        }
    }
}
