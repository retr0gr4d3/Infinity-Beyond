using UnityEngine;

namespace Infinity_TestMod
{
    public class BeyondLifecycle : MonoBehaviour
    {
        private static BeyondLifecycle _instance;

        public static void Create()
        {
            if (_instance != null) return;
            
            UnityEngine.Debug.Log("[Beyond] Bootstrapping standalone agent...");

            var go = new GameObject("BeyondAgent");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<BeyondLifecycle>();
            
            // Initialize the TestMod
            TestMod.Initialize();
        }

        private void Update()
        {
            if (TestMod.activeInstance != null)
            {
                try { TestMod.activeInstance.OnUpdate(); } catch { }
            }
        }

        private void OnGUI()
        {
            if (TestMod.activeInstance != null)
            {
                try { TestMod.activeInstance.OnGUI(); } catch { }
            }
        }

        private void OnApplicationQuit()
        {
            if (TestMod.activeInstance != null)
            {
                try { TestMod.activeInstance.OnApplicationQuit(); } catch { }
            }
        }
    }
}
