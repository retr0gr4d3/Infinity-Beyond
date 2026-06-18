# Beyond - Standalone Client

A custom launcher and in-game mod for **AdventureQuest Worlds Infinity**. The
launcher embeds the Unity game inside its own window, runs multiple accounts side
by side, and exposes a set of tools (cosmetic spoofers, autoskills, packet
sniffer/sender, quest automation, and more) that talk to a mod injected into the
game.

> This is a third-party tool intended for local, single-player experimentation and
> learning. Use it responsibly and at your own risk.

---

## How it works

The project is two cooperating pieces plus the game itself:

```
┌───────────────────────────┐         named pipe               ┌───────────────────────────┐
│  Launcher (Avalonia app)  │  ◄────  BeyondAgent_<guid> ────► │  BeyondAgent (game mod)   │
│  BeyondLauncher.exe       │         JSON, newline-           │  injected into the game   │
│  • embeds the game window │         delimited                │  • applies settings       │
│  • tool windows / UI      │                                  │  • runs commands          │
│  • per-session view-model │                                  │  • streams status back    │
└───────────────────────────┘                                  └───────────────────────────┘
            │ spawns + HWND-parents                                  ▲ injected into
            ▼                                                        │ Assembly-CSharp.dll
┌────────────────────────────────────────────────────────────────────────────────────────┐
│  AdventureQuest Worlds Infinity (Unity, Mono)                                          │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

- **Launcher** (`Beyond/Launcher/`)  an [Avalonia](https://avaloniaui.net/) desktop app
  (.NET 10, win-x64). It spawns the game, re-parents the game's window into a
  session tab, and drives everything from one view-model per session.
- **BeyondAgent** (`Beyond/BeyondAgent/`) - the mod (.NET Standard 2.1, [Harmony](https://github.com/pardeike/Harmony)).
  On build it is copied into the game's `Managed` folder; the launcher then patches
  the game's `Assembly-CSharp.dll` with Mono.Cecil to call
  `Infinity_TestMod.BeyondLifecycle.Create()` from `AEC.Start()`, which boots the
  mod. The mod and launcher speak over a per-session named pipe.
- **The game** - your AdventureQuest Worlds Infinity install, in any location. The
  launcher patches and embeds it; you point at it from the Configurator. It is not
  bundled with the source (and is git-ignored). Release names differ, so nothing
  assumes a fixed folder or executable name - the launcher runs whatever game
  executable it finds in the configured directory.

Each launcher session mints a unique pipe name, launches the game with that pipe
in the environment, and connects to it. The mod mirrors its full settings snapshot
back to the launcher so every tool window reflects live game state.

---

## Features

- **Multi-account sessions** - launch several accounts at once, each an embedded
  game in its own tab; all keep running while you switch between them.
- **Configurator** - store accounts (with nicknames) and the game directory.
- **Auto-launch** - fills the login screen and advances the play screen straight to
  server select.
- **Tool windows** - Visual Spoofers &amp; Jukebox, Autoskills, Quest Loader, Quest
  Runner &amp; Chain Editor, Shop Loader, Fake Dev, Packet Sniffer / Interceptor /
  Sender / Receiver.

---

The game install lives outside the repo (git-ignored); point at it from the
Configurator at runtime and via the build script at build time.

---

## Requirements

- **Windows** (the launcher re-parents the native game window via Win32).
- **.NET 10 SDK**.
- A copy of **AdventureQuest Worlds Infinity** installed somewhere (the mod is
  compiled against the game's managed assemblies).

## Build

The simplest path is the build script, which prompts for your game directory,
builds, publishes, and deploys the launcher to the repo root:

```bat
build.bat
```

To build with the SDK directly, tell the mod build where the game is - either set
`AQWI_GAME_DIR`, or pass the managed folder explicitly:

```sh
dotnet build Beyond.sln -c Release -p:AqwiManagedDir="<game>\<name>_Data\Managed"
```

Building `BeyondAgent` copies `BeyondAgent.dll` (and Harmony) into the game's
`…_Data/Managed` folder. The launcher patches `Assembly-CSharp.dll` on first launch
(making a `.dll.bak` backup; it skips patching if MelonLoader is detected).

## Run

1. Start `BeyondLauncher.exe` (from the repo root after running `build.bat`).
2. On the **Configurator** tab, set the **game directory** and add one or more
   accounts.
3. Press **Launch** on an account (or **+ Add Session**). If no game executable is
   found in the configured directory, the launcher warns you instead.

---

See [CONTRIBUTING.md](CONTRIBUTING.md) for how the code is organized and a full,
worked walkthrough of adding a feature.
