// InputPatch has been removed because the launcher is a separate process/window structure
// and we no longer need to hook UnityEngine.Input inside the game client to block mouse over UI.
// This resolves the Harmony patching exception on static UnityEngine.Input.GetMouseButton.
