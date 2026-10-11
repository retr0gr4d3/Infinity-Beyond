Run against the current game's Managed directory:

```powershell
dotnet run --project Beyond/AssemblyPatcherChecks -c Release -- "C:\path\to\game\AdventureQuest Worlds Infinity_Data\Managed"
```

The runner patches disposable copies only. It accepts an already-patched client
and checks the current client rewrite, exact-version and legacy backups
(including ones truncated by an interrupted launch), repeat launches, missing
dependencies, a locked destination, a game update landing mid-patch, the
MelonLoader restore, and staged-file cleanup.

A clean install has no `BeyondAgent.dll` in Managed until the launcher copies
one in. Build `Beyond/BeyondAgent` first; its `Beyond/build` output is copied
beside the runner and used when Managed has none.

The missing AudioModule case reproduces the October 2026 failure: Cecil resolved
an enum parameter while writing metadata, threw an AssemblyResolutionException,
and the old direct-to-file writer left Assembly-CSharp.dll empty. The repaired
patcher resolves dependencies from Managed, validates its output in memory, and
atomically replaces the installed file only after successful serialization.
