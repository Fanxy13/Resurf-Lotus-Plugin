# Resurf – a Lotus plugin

`/surf` puts the Unity game [Subway Surfers Resurferd](https://github.com/cyberillcbutsmarter/subway-surfers-resurferd)
on your Mac and starts it – a plugin for [Lotus](https://github.com/Fanxy13/homebrew-lotus).

```
lotus plugin install https://github.com/Fanxy13/Resurf-Lotus-Plugin
lotus surf
```

The first `lotus surf` asks once, then gets the game, makes it run on macOS and starts it. After that, `lotus surf`
just starts it.

## What it does

The game is a Unity 2019.4 project. Two ways bring it to your Mac:

1. **A ready Mac build** – the `…-StandaloneOSX.zip` of the newest
   [GitHub release](https://github.com/cyberillcbutsmarter/subway-surfers-resurferd/releases) that has one, checked
   against the SHA-256 GitHub's API gives for it. GitHub's API answers 60 questions an hour without an account; when
   a shared network (a school, an office) has used them up, Lotus reads GitHub's release pages instead – those give
   no SHA-256, and Lotus says so.
2. **Built here, when no release has one** – the source comes with git, and Unity 2019.4.41f2 (the version the
   project is made with) builds the Mac app in the background while the lotus blooms: the assets it imports, the
   scripts it compiles, the player it builds. Unity Hub installs that Unity for you – Lotus offers Unity Hub from
   Homebrew if you have none. The first build takes a while; later ones are quick, Unity keeps its cache.

Either way, Lotus then does what a Mac needs before it opens a game like this:

- **its program executable** – builds made on Linux (the game's CI) are not
- **out of quarantine** – a downloaded app is
- **signed for this Mac** – an ad-hoc signature, Linux cannot sign
- **Rosetta 2 on Apple silicon** – Unity 2019 makes Intel apps; macOS offers Rosetta the first time, and Lotus
  offers it before Unity runs

## The Resurf mod

Lotus puts a small mod into the game – [mod/src/Resurf.cs](mod/src/Resurf.cs), built into
[mod/ResurfMod.dll](mod/ResurfMod.dll). [Unity Doorstop](https://github.com/NeighTools/UnityDoorstop) loads it when
the game starts (the loader BepInEx uses; LGPL-2.1, its macOS build in [mod/doorstop](mod/doorstop) with its
license and where it came from). The game's program becomes a small launcher that sets Doorstop up and starts the
real one, so the Finder, the Dock and `lotus surf` start it as before. Nothing of the game's own code changes, and
`lotus surf mod off` gives the game its own program back.

- **The coins' glow** – the project draws it with a spray-paint shader that blends, so the dark pixels around the
  glow make a black edge. The mod draws it with the project's additive shader, as a glow is drawn (the mystery
  box's glow too).
- **Coins from Lotus** – `lotus surf coins 5000`: a running game has them within a second, a closed one when it
  starts.
- **A save that holds** – the game itself saves only after a run's coins are counted (and when a phone sends it
  away). The mod also saves every 20 seconds, when the window loses focus and when you quit (⌘Q), and writes a
  readable copy, `resurf/save.txt`, next to the game's own `playerdata` in
  `~/Library/Application Support/unity.Kiloo Games.Subway Surf` – `lotus surf stats` shows it.
- **An FPS counter** – bottom left, green from 50, yellow from 30, red below: `lotus surf fps`, or **F** in the game.
- **A cape** – cosmetics for the runner, with two designs the mod draws itself: **Inferno**, flames that boil and
  end in tongues of fire, and **Galaxy**, a night sky with twinkling stars, a golden crescent and a golden trim. It
  hangs from the character's neck (or back), swings with the cloth, trails when the runner runs and flutters. Choose
  it, its size and its height in the menu, or `lotus surf cape inferno|galaxy|off`. The mod finds the character by
  the names of its bones; the menu says where the cape hangs, and *Other way round* turns it if it ever sits at the
  front.
- **A menu in the game** – **M** or **F1** opens it (Esc or the same key closes it): the FPS counter, the coins' glow,
  the cape, the game's speed (0.25x–3x), how often the mod saves (never, 10, 20 or 60 seconds), *Save now* and coins
  (+100, +1,000, +10,000). What you set is kept in the `settings` file of the mod (`FPS`, `GLOW`, `SPEED`, `AUTOSAVE`, `CAPE`, `CAPE_SIZE`, `CAPE_HEIGHT`, `CAPE_FLIP`) and
  is there at the next start. A click on the menu can reach the game behind it, so use it on the start screen or
  while the game is paused.

GitHub Actions builds the DLL against the libraries of the game's own Mac build, takes Doorstop's official macOS
build (checked against GitHub's SHA-256), tests both in the game – coins that waited, coins while it runs, the glow,
the coins still there after a restart, the game started by macOS as the Dock does – and only then puts them into
the repository. `zsh mod/build.zsh <the app>` builds the DLL yourself (with .NET's `csc` or Mono's `mcs`).

## Commands

| | |
|---|---|
| `lotus surf` | play – the first time it gets the game (it asks first) |
| `lotus surf play --fullscreen` | or `--window` |
| `lotus surf get` | the game now, without the question – for scripts too |
| `lotus surf update` | a newer release or a newer source: the game again |
| `lotus surf build` | build it from the source with Unity, even when a release has a Mac build |
| `lotus surf install <zip\|app>` | a Mac build you downloaded yourself |
| `lotus surf status` | where it is, where it came from, what it needs |
| `lotus surf log` | what Unity said in the last build |
| `lotus surf remove` | the game and its source go (it asks) – your progress in the game stays |
| `lotus surf coins 5000` | 5000 coins more in the game – `coins` alone shows them, `coins set 100000` sets them |
| `lotus surf stats` | coins, high score, best distance, mystery boxes, missions – as the game saved them |
| `lotus surf fps` | the FPS counter on or off (bottom left; **F** in the game switches it too) |
| `lotus surf cape inferno\|galaxy\|off` | a cape for the runner – size, height and more in the game's menu (**F1**) |
| `lotus surf glow fix\|original` | the coins' glow without the black edge (the default), or as the project draws it |
| `lotus surf mod on\|off` | the Resurf mod in the game, or the game as it is |

`lotus resurferd …` is the same as `lotus surf …`, and in a Lotus shell both work as `/surf`. The AI in `/ai` can
start the game too.

In the game: **← →** or **A D** change lanes, **↑** or **W** jumps, **↓** or **S** rolls, **Space** or a click twice
is the hoverboard.

## What it needs

- [Lotus](https://github.com/Fanxy13/homebrew-lotus) 2.18.3 or newer: `brew install Fanxy13/lotus/lotus`
- git (`xcode-select --install`) and jq – macOS 15 has it, older ones: `brew install jq`
- To build: Unity 2019.4.41f2 with a Unity license. The free **Personal** license is enough – open Unity Hub,
  sign in, then *Preferences → Licenses → Add → Get a free personal license*. Unity needs it even to build from the
  command line, and Lotus says so when it is missing.

## Where things are

Everything is in the plugin's data folder, `~/.local/share/lotus/plugins/resurferd`:

| | |
|---|---|
| `Subway Surfers Resurferd.app` | the game |
| `source/` | the Unity project, with Unity's cache (`Library`) |
| `build.log` | what Unity said in the last build |
| `settings` | yours to change, one `KEY=value` per line |

| Setting | |
|---|---|
| `REPO=owner/name` | the game from a fork |
| `GET=auto\|release\|build` | how the game comes: a release when there is one, else built (auto); only a release; always built |
| `UNITY=/path/to/Unity.app` | a Unity that Unity Hub does not know |

`lotus plugin uninstall resurferd` takes the data folder with it. Unity and Unity Hub stay – Unity Hub removes them.

## Why full access

Lotus runs plugins in a sandbox unless they ask for more. This one asks for full access because Unity needs your
Unity license, which lives in `~/Library`, and the game has to be opened by macOS – a sandboxed plugin can do
neither. What it downloads and builds stays in its data folder; it asks before it installs Unity Hub, Unity or
Rosetta 2.

## When something goes wrong

| Lotus says | |
|---|---|
| *Unity has no license on this Mac* | sign in to Unity Hub and add the free personal license, then `lotus surf build` |
| *Another Unity has the project open* | close that Unity, then `lotus surf build` |
| *The game's scripts did not compile* | Unity's errors are shown; `lotus surf log` has all of it – the source may have changed, `REPO=` can point to a version that works |
| *Unity is not installed yet* | Unity Hub opened with it – install it there, then `lotus surf build` |
| macOS: "cannot be opened" | `lotus surf install` the app again – it is made executable, unquarantined and signed again |

## Tests

```
zsh tests/run.zsh [Lotus folder]     the plugin with a fake release, a fake Unity and a git source on this Mac
zsh tests/game.zsh [Lotus folder]    the real game: the newest release's Mac build, made to run and started
```

Without a folder they use the `lotus` on your PATH. GitHub Actions runs both on macOS for every push
([.github/workflows/test.yml](.github/workflows/test.yml)).

The game is not part of this repository – the plugin gets it from the game's own repository.
