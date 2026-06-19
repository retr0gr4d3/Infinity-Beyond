using UnityEngine;

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

            UnityEngine.Debug.Log("[Beyond] Bootstrapping standalone agent...");

            GameObject go = new("BeyondAgent");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<BeyondLifecycle>();

            // Initialize the TestMod
            BeyondAgentClass.Initialize();
        }

        private void Update()
        {
            if (BeyondAgentClass.activeInstance != null)
            {
                try { BeyondAgentClass.activeInstance.OnUpdate(); } catch { }
            }
        }

        private void OnGUI()
        {
            if (BeyondAgentClass.activeInstance != null)
            {
                try { BeyondAgentClass.activeInstance.OnGUI(); } catch { }
            }
        }

        private void OnApplicationQuit()
        {
            if (BeyondAgentClass.activeInstance != null)
            {
                try { BeyondAgentClass.activeInstance.OnApplicationQuit(); } catch { }
            }
        }
    }
}
