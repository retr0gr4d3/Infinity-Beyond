# aPatcher - Beyond for Android

Patches the AQW Infinity Android APK so it loads Beyond, without hard-coding
anything that changes between game releases.

```
patch.bat                       # patch the newest APK in "..\Android APK"
patch.bat --install             # ...and push it to a connected device
python patch_apk.py --check out.apk
```

Nothing here touches the Windows/macOS build. The PC launcher and agent are
unchanged. This is explained later in the technical breakdown.

---

## Building it yourself

### 1. What you need

| Tool | Why | Notes |
|---|---|---|
| **Python 3** | runs the patcher | on `PATH`; no packages to install |
| **A JDK** | `keytool`, to make the signing key | found via `PATH` or `JAVA_HOME`; JDK 26 used here |
| **Android SDK build-tools** | `aapt2`, `zipalign`, `apksigner` | highest installed version is picked automatically |
| **Android NDK** (r29 used here) | compiles the shim | only needed when the shim changes - see `--no-build` |
| **adb** (`<sdk>/platform-tools`) | only for `--install` | optional |

Nothing else. There is **no third-party hooking library** to fetch - see *Why
the hooker is hand-written* - and the patcher imports only the Python standard
library.

The SDK is located via `ANDROID_SDK_ROOT`, then `ANDROID_HOME`, then the
per-OS default (`%LOCALAPPDATA%\Android\Sdk`,
`~/Library/Android/sdk`, `~/Android/Sdk`). The NDK is located via
`ANDROID_NDK_ROOT` / `ANDROID_NDK_HOME` / `NDK_ROOT`, else the
highest-numbered directory under `<sdk>/ndk/`.

### 2. Installing the NDK

With Android Studio, use SDK Manager → SDK Tools → NDK. From the command line,
if you have `cmdline-tools`:

```
sdkmanager "ndk;29.0.14206865"
```

If you do **not** have `cmdline-tools` (a plain Android Studio install often
does not), download the NDK directly and unzip it into `<sdk>/ndk/`. The
extracted `android-ndk-r29` works as-is - the patcher picks the
highest-numbered directory - but renaming it to the version string keeps
`sdkmanager` consistent if you use it later:

```
https://dl.google.com/android/repository/android-ndk-r29-windows.zip
sha1 ab3bb30fbb9e6903666d60c55d11e78b04e07472   (834 MB)
```

Swap `windows` for `darwin` or `linux` on other hosts. Google's authoritative
list of NDK URLs, sizes and checksums is
`https://dl.google.com/android/repository/repository2-3.xml`.

### 3. Get the game APK

Put a stock `aq2d.apk` in `Android APK/` next to this folder. It is **not** in
the repo - it is ~110 MB and not ours to redistribute, and `.gitignore` excludes
`*.apk`. The patcher picks the newest `.apk` there that is not already a
`-beyond.apk`.

### 4. Build and patch

```
patch.bat                          # Windows
python patch_apk.py                # any OS - patch.bat just forwards to this
```

That compiles the shim for every ABI in the APK, repacks, aligns, signs and
self-checks, leaving `Android APK/aq2d-beyond.apk`. It takes about 8 seconds.

On the first run it creates `beyond.keystore` (password `beyond`). Keep that
file: it is what lets later builds install over earlier ones without an
uninstall. It is git-ignored, being a signing key.

Useful flags:

```
python patch_apk.py path/to/game.apk -o out.apk   # explicit input/output
python patch_apk.py --no-build                    # reuse shim/out, skip the NDK
python patch_apk.py --install                     # adb install the result
python patch_apk.py --check out.apk               # verify an existing APK
```

### 5. Install

```
adb uninstall com.Artix.aq2d      # FIRST patched build only - deletes app data
patch.bat --install
```

Re-signing changes the APK signature, so the **stock** game has to go before
the first patched build lands. The patcher prints that command rather than
running it, because it wipes the app's local data. Later patched builds install
straight over each other - the key is stable.

After that, always `am force-stop` **before** reinstalling; replacing the APK
under a live process crashes it inside `libunity.so`.

### 6. The edit / test loop

```
adb shell am force-stop com.Artix.aq2d
python patch_apk.py --install
adb logcat -G 32M                                  # once per boot; see below
adb logcat -c && adb logcat -s Beyond > log.txt &  # start BEFORE launching
adb shell am start -n com.Artix.aq2d/com.unity3d.player.UnityPlayerActivity
```

Start the log capture *before* launching: the game is verbose enough to roll
the default ring buffer past the shim's startup lines within a minute, which
looks exactly like the shim never ran. See *Reading the log* for what the
output means.

### 7. Files

```
patch_apk.py          the patcher: toolchain discovery, shim build, zip surgery,
                      align, sign, self-check
patch.bat             Windows wrapper (named patch.bat, not build_android.bat,
                      because .gitignore has a broad *build* rule)
shim/beyond_shim.c    the whole mod: loader, ARM64 hooker, il2cpp glue, menu
shim/out/<abi>/       compiled shims (git-ignored)
beyond.keystore       local signing key, created on first run (git-ignored)
ANTICHEAT.md          what anticheat is (and is not) in the stock APK, measured
```

---

# Technical breakdown

## What the Android release actually is

Measured from `Android APK/aq2d.apk` (version 0.0.254, versionCode 9):

| | |
|---|---|
| Package | `com.Artix.aq2d` |
| Launcher activity | `com.unity3d.player.UnityPlayerActivity` (stock, no subclass) |
| Unity | **6000.3.17f1** (Unity 6.3) |
| Scripting backend | **IL2CPP** - `libil2cpp.so`, `global-metadata.dat` |
| IL2CPP metadata | **version 39**, unencrypted (sanity `0xFAB11BAF`) |
| ABIs | `arm64-v8a`, `armeabi-v7a` |
| minSdk / targetSdk | 25 / 36 |
| `extractNativeLibs` | `true` - native libs land on disk, so `dlopen` by name works |
| `<application android:name>` | absent |

The one fact the patcher is built on: **`libmain.so` exports exactly one
symbol, `JNI_OnLoad`**, and Unity's Java side reaches it through a plain
`System.loadLibrary("main")` (confirmed in `classes.dex`).

## Why the desktop agent does not just port

`Beyond/BeyondAgent` is ~14.6k lines of C# that Harmony-patches a **Mono**
`Assembly-CSharp.dll`. On Android there is no `Assembly-CSharp.dll` - IL2CPP
has already compiled that C# to native ARM inside `libil2cpp.so`. Harmony has
nothing to patch.

The usual bridge is Il2CppInterop (MelonLoader / LemonLoader / BepInEx), which
reconstructs .NET assemblies from the IL2CPP metadata and lets C# mods run more
or less unchanged. That path is **blocked today**: Cpp2IL, which those loaders
depend on, supports metadata versions 23–31, and this build is **39**.
Il2CppDumper has the same gap. Only experimental community forks claim v39, and
none of them ship an Android runtime.

So the C# agent is on hold until the interop tooling catches up with Unity 6.3.
What is *not* blocked is everything below, because `libil2cpp.so` exports the
IL2CPP C API (`il2cpp_domain_get`, `il2cpp_class_from_name`,
`il2cpp_class_get_method_from_name`, …) and those resolve types **by name**,
which is completely independent of the metadata version.

## How the patch works

```
lib/<abi>/libmain.so   ──rename──▶  lib/<abi>/libmain_orig.so   (Unity's real loader)
shim/beyond_shim.c     ──build───▶  lib/<abi>/libmain.so        (ours)
```

`System.loadLibrary("main")` now reaches our shim. It spawns a background
thread, then `dlopen`s `libmain_orig.so` and forwards `JNI_OnLoad`, so Unity
boots exactly as before. On the background thread:

1. Wait for `libil2cpp.so` to be **mapped** (`dlopen` with `RTLD_NOLOAD`, which
   never calls into it).
2. Hook `il2cpp_init` and wait for it to return. That is the readiness signal -
   see *Two traps* below for why the obvious alternative crashes.
3. Resolve `AEC.GetResponse` - the same method the desktop agent patches in
   `Patches/AECPatch.cs` - and hook it, logging the class name and
   `GetCommand()` of every packet the client receives:

```
adb logcat -s Beyond
```

**Nothing else in the APK is modified.** No `AndroidManifest.xml` edit, no
`classes.dex` edit, no `global-metadata.dat` edit, no addresses, no offsets.
That is what makes it survive future releases: a new `aq2d.apk` drops in and
the same shim patches it.

The patcher then `zipalign`s, re-signs with a local key (`beyond.keystore`,
auto-created on first run, password `beyond`), and self-checks the result:
every ABI must have a stashed original and a shim that differs from it, and the
signature must verify.

### Two traps, both hit on device

**`il2cpp_domain_get()` is not safe to call before `il2cpp_init` has run.** It
dereferences runtime state that does not exist yet. Polling it as an "is the
runtime up?" signal segfaults the game on a cold start - `SIGSEGV` reading
`0x135`, inside `il2cpp_domain_get` itself. Hook `il2cpp_init` and use its
return instead.

**A `B` cannot reach from `libmain.so` to `libil2cpp.so`.** Android maps them
about **2.8 GB** apart, far outside a branch's ±128 MB. The shim therefore
`mmap`s a page *near the target* (`MAP_FIXED_NOREPLACE`, searching outward in
64 KB steps) holding a branch island plus the trampoline, and branches to that.

The island is also what keeps the patch to a **single 4-byte store**, so only
one instruction is displaced and a thread mid-call sees either the old
instruction or the branch, never a mixture. A 16-byte absolute jump would
displace four - and IL2CPP prologues routinely carry an `ADRP` within the first
four. `AEC.GetResponse` does exactly that, at word 3.

### Why the hooker is hand-written

ShadowHook 2.0.1 - the obvious dependency - **cannot initialise on Android 17**:
`shadowhook_init` returns `SHADOWHOOK_ERRNO_INIT_LINKER` (12) because it parses
dynamic-linker internals that moved, and 2.0.1 is the newest release. Dobby's
latest release ships iOS/macOS prebuilts only. So `shim/beyond_shim.c` carries
~90 lines of ARM64 hook instead, doing the one thing needed and **refusing
rather than guessing**: it declines any entry instruction that is PC-relative,
and declines if the island lands out of range. Both refusals log the offending
instruction.

Re-check ShadowHook when it next releases; if it starts initialising, deleting
the hand-rolled hooker is a small and welcome diff.

### Verified

Against 0.0.254: all 1272 entries copied through byte-identical, the manifest
reads unchanged, re-patching an already-patched APK is idempotent, and the run
takes ~8s. The built shim is ELF64/AArch64 and ELF32/ARM, each exporting only
`JNI_OnLoad` - the same export surface as the `libmain.so` it replaces.

On device (Pixel 11 Pro, Android 17, arm64-v8a) the game boots, both hooks
install, and no process crashes:

```
Beyond: libil2cpp.so mapped after 20 ms
Beyond: hook il2cpp_init: entry a9bf4ffe at 0x6c63f6df00
Beyond: attached: domain=0x6fac81afc0 image=0x6ba02495e8
        AEC=0x6ba0499820 GetResponse=0x6ba033cb00 code=0x6c64329100
Beyond: hook AEC.GetResponse: entry d10103ff at 0x6c64329100
Beyond: hooked AEC.GetResponse - logging packets
Beyond: Beyond loader ready
```

That is the proof that name-based il2cpp resolution and inline hooking both
work on Unity 6.3 / metadata v39 - no dumper, no offsets.

Packets, from a logged-in session - same `Type (command)` shape as the desktop
sniffer:

```
Beyond: packet ResponseLogin (loginResponse)
Beyond: packet ResponseInitPlayer (initPlayer)
Beyond: packet ResponseAddOrUpdateItems (addItems)
Beyond: packet ResponseQuestData (questData)
Beyond: packet ResponseCellJoin (CellJoin)
```

The menu, drawing over the live game:

```
Beyond: hook AEC.Update: entry d10183ff
Beyond: hooked AEC.Update - menu setup queued for the main thread
Beyond: menu: host PostProcessDebug attached, OnGUI hooked
Beyond: menu: first draw ok
```

A `GUI.Box` reading "Beyond: hooked" renders in the top-left corner in game.

**Logcat note:** the game is verbose enough to roll the default ring buffer
past these lines within a minute. Use `adb logcat -G 32M`, or stream
`adb logcat -s Beyond` to a file *before* launching, rather than dumping with
`-d` afterwards.

Reinstall note: `am force-stop` **before** `adb install -r`, not after.
Replacing the APK under a live process crashes it inside `libunity.so` via
`bitter.jnibridge` - that failure looks alarming in logcat and has nothing to do
with the shim.

### How the on-device menu draws

`unity.strip-engine-code=true` did **not** remove IMGUI - `GUILayout` and `GUI`
resolve at runtime. But GUI calls are only legal inside an `OnGUI`, and the
game's own code declares none. The probe found two shipped library types that
do:

```
imgui probe: OnGUI on UnityEngine.Rendering.PostProcessing.PostProcessDebug
imgui probe: OnGUI on UnityEngine.Purchasing.UIFakeStoreWindow
imgui probe: scanned 14551 classes in 103 assemblies, 2 OnGUI method(s)
```

So the menu borrows one. `setup_menu` creates a `GameObject`, keeps it with
`DontDestroyOnLoad`, attaches `PostProcessDebug` via the `AddComponent(Type)`
overload, and hooks that type's `OnGUI` - which puts our draw code inside a
valid GUI context. The original `OnGUI` is deliberately **not** called: we
created the only instance, nothing else in the game uses the type, and its own
code would run against fields we never set.

**Unity refuses `GameObject` creation off the main thread**, so this cannot run
on the loader's background thread. It runs from a hook on `AEC.Update`, a
MonoBehaviour tick that is main-thread by definition. That hook is also where
anything needing a per-frame timer belongs - autoskills, for one.

Overloads need care: `il2cpp_class_get_method_from_name` returns the first
name+argc match, which for `GUI.Box` is the `(Rect, Texture)` sibling. The
`find_method` helper matches a parameter's type name as well.

### What strip-engine-code actually left

This is a property of *this build*, not of Unity, so it was measured rather
than assumed (`probe_api` logs full signatures):

| Wanted | Reality in this APK |
|---|---|
| `GUI.Box(Rect, string)` | survives |
| `GUI.Label(Rect, string)` | survives |
| `GUI.Button(Rect, string)` | **gone** - only `(Rect, GUIContent, GUIStyle)` |
| `GUI.TextField` | **gone entirely**, from `GUI` *and* `GUILayout` |
| `GUILayout` | only 9 methods: `Button/2`, `Label/2`, `Width`, `Height`, scroll views |

So buttons wrap their text in a `GUIContent` and borrow `GUI.skin.button`, and
text input comes from `TouchScreenKeyboard` - the OS keyboard, which is the
right control on a phone anyway. `GUI.matrix` scales the whole UI including
fonts (`Screen.dpi / 160`, clamped 1–4; 2.62 on the test device), which sizing
rects alone would not do.

### Two more native-reflection traps

`il2cpp_class_get_field_from_name` does **not** walk base classes, and
`find_method` (built on `il2cpp_class_get_methods`) enumerates a class's own
methods only. `Entity.Name` is a virtual property declared on `Entity`, so
looking for it on `Player` silently returns null either way. Resolve inherited
members against the class that declares them; invoking a virtual still
dispatches correctly on the derived instance.

### GC handles are pointer-width on Unity 6

`il2cpp_gchandle_new` is declared `uint32_t` in the classic il2cpp headers. On
this runtime it returns a **pointer**. Storing it in a `uint32_t` truncates it,
and `il2cpp_gchandle_get_target` then dereferences the truncated value and
segfaults - the fault address is the truncated handle, which is what gives it
away. Declare handles as `void *`.

The shim keeps exactly one GC handle now (the live `TouchScreenKeyboard`);
command text lives in a plain C buffer and the button style is fetched per
call, because neither needed to survive a frame boundary.

### Reading the log

| Log line | Meaning |
|---|---|
| `hooked AEC.GetResponse` then `Beyond loader ready` | working |
| `packet <Type> (<cmd>)` | a received packet |
| `libil2cpp.so never appeared` | the 60s wait expired; the game probably failed to start |
| `il2cpp_init never completed` | hooked too late, or the runtime never came up |
| `entry … is PC-relative - refusing` | prologue changed; the hooker must learn to relocate that form |
| `no free page within B reach` | address space too crowded near `libil2cpp.so` |
| `code pointer … is not inside libil2cpp.so` | `MethodInfo` layout changed; see `method_code_ptr` |
| `AEC has no 0-arg GetResponse` | signature changed this release |
| game does not boot | shim failed to forward `JNI_OnLoad`; reinstall the stock APK and read logcat unfiltered |

---

### Adding a feature

The shim resolves everything by name at runtime, so a new feature is: find the
class and method in the decompiled desktop `Assembly-CSharp.dll` (`ilspycmd`
against your PC install), resolve it in `setup_menu` with `find_method`, and
either call it through `inv()` or hook it with `hook_func`. `probe_api` is
there to dump real signatures from the device when the desktop build and the
Android build disagree - which they do, because `strip-engine-code` removes
whatever the game never calls.

## What is left to build

In dependency order:

1. ~~A hook engine in the shim.~~ Done - a hand-rolled ARM64 inline hook on the
   code pointer from `MethodInfo`, validated with `dladdr` before use.
2. ~~A way to draw on device.~~ Done - see *How the on-device menu draws*.
3. ~~The packet tools.~~ Done. Tools panel: **Block** (wired to the
   `GetResponse` hook - blocking is `return NULL`), **Clear**, **Type** (opens
   the OS keyboard), **Send** (builds `Request(cmd)` and calls
   `AEC.sendRequest`, the desktop Packet Sender's path), **Log** and **Help**,
   each a separate window so the panel stays readable. `AEC.sendRequest` is
   hooked too, so outgoing traffic is logged and the live `AEC` instance is
   captured for Send to use.

   **Help** paginates all 73 commands, extracted from the `Request` subclasses
   in the decompiled `Assembly-CSharp`; tapping one loads it into the send box,
   so nobody has to remember wire names.
4. ~~Autoskills and name spoof.~~ Done.
   - **Autoskills** cycles slots 0-4 on the `AEC.Update` tick via
     `UISkillSlots.GetSlot(i)` then `SkillSlotButton.UseSkill(true)`/`(false)`.
     `UISkillSlots` derives from `Singleton<T>`, whose static `Instance` sits on
     an inflated generic type that is awkward to reach from native - so the
     instance is captured by hooking `UISkillSlots.Register` instead.
   - **Name/title spoof** writes into `Player.nameTagView` (a `NameplateView`)
     via `SetName`/`SetTitle`/`SetTitleVisible`, re-asserted once a second
     because the game rebuilds nameplates on every map change. The top-left HUD
     panel is a second surface with its own hook: `UIPlayerPanel.setText()`
     re-runs `nameText.text = target.Name` every Update, so it is overwritten
     after the fact via `TMP_Text.set_text`, guarded to `Entity.mainPlayer`.
     Display only - the real `Entity.Name` is never mutated, because other code
     paths still depend on it.

   *Verified on device:* every hook installs, the menu renders and paginates,
   no crashes. *Not verified:* Send delivering a live command, autoskills
   actually casting, and the spoof rendering - all three need a logged-in
   session in combat.
5. **The rest of the tools.** `AEC`'s surface maps almost one-to-one onto
   Beyond's desktop tools:

   | `AEC` method | Beyond feature |
   |---|---|
   | `sendRequest/1`, `sendMessage/1` | Packet Sender - no hook, just `runtime_invoke` |
   | `queueResponse/1`, `WrapAndQueueResponse/1` | Packet injection / fake responses |
   | `GetResponse/0` *(already hooked)* | Interceptor - returning null **is** blocking |
   | `add_RawRequestSent`, `add_RawResponseReceived` | Outgoing traffic, already evented |
   | `EncryptDecrypt/1`, `Serialize/Deserialize/1` | Raw wire access |
   | `connect/3`, `close/0`, `Disconnect/0`, `get_HasSocket/0` | Connection control |

   Next up, with the call paths already read off the desktop agent and the
   decompiled `Assembly-CSharp`:

   - **Autoskills** - `UISkillSlots.Instance.GetSlot(i)` then
     `SkillSlotButton.UseSkill(true)`/`(false)`, driven off the `AEC.Update`
     tick that already exists.
   - **Name/title spoof** - hook `Player.ComposeNameplateText()` (private,
     0-arg, returns string) and return the spoofed text, then call
     `RefreshNameplate()`. The desktop agent does exactly this.

   The hitbox overlay needs a renderer and should come last.
6. **Revisit the C# agent when Cpp2IL supports metadata 39.** If Il2CppInterop
   gains Unity 6.3 support, most of `BeyondAgent` becomes portable again and
   much of the above collapses into a loader that starts a .NET runtime. Worth
   re-checking before investing heavily in native ports.

There is no launcher process on Android and no named pipes, so the Avalonia
tool windows do not apply. `BeyondAgentClass.OnGUI` - the desktop agent's
existing IMGUI menu, gated behind its `useImgui` flag - is the layout to copy,
but it is C# and cannot run, so it is a model to reimplement rather than code
to port.

A cheaper partial alternative worth knowing about: the packet features could be
done entirely outside the game with a local proxy, no APK patching at all. It
would not cover spoofers, autoskills or the overlay.

## Caveats

- The APK ships **no anticheat**: no anti-tamper, no root enforcement, no
  debugger detection. That was measured, not assumed - see
  [ANTICHEAT.md](ANTICHEAT.md) for the evidence and the repro commands.
- The build bundles GameAnalytics, whose stock telemetry annotates its events
  with device and build metadata of its own accord. Not gameplay data, not
  Artix's code, and nothing in the game reads it back - but it is the one thing
  in the APK that describes your setup, so read
  [ANTICHEAT.md](ANTICHEAT.md) §2 once.
- `libsteam_api.so` is present and unused on Android.
- `patch_apk.py --check <apk>` is the self-check and is run automatically after
  every patch. It fails on an unpatched APK, which is how you know it has teeth.
