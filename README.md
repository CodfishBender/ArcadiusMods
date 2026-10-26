# Arcadius ModKit

A small mod loader for Arcadius (Unity 6000.4, Mono): drop-in mods with Harmony patches, config files,
ready-made gameplay hooks, and texture, audio and text replacement. The game's own DLLs stay original.

This repo holds only the source; the game is a separate folder. The scripts find it from `--game PATH`
(remembered in `game.path`), then `$ARCADIUS_GAME`, then `game.path`, then `../Arcadius` next to this repo.

```
./install.sh [--game PATH] [MOD...]   build the loader and the mods (default: all), publish them into
                                      the game's Mods folder (keeps your configs and enabled flags)
./uninstall.sh [--game PATH]          restore the game's original files (leaves Mods/ alone)
```

For IDE builds, set `ARCADIUS_GAME` or keep the game at `../Arcadius`.

In game: **F9** reloads every `config.json` and asset folder, **F10** dumps loaded assets to `Mods/_dump`.
Log: `Mods/modlog.txt` (and Player.log).

## How it starts

`install.sh` copies `ArcadiusModLoader.dll` and `0Harmony.dll` (Harmony 2.4.3, MIT) into
`Arcadius_Data/Managed`, registers the loader in `ScriptingAssemblies.json`, and adds two entries to
`RuntimeInitializeOnLoads.json`. Unity then calls `Bootstrap.Init` after assemblies load, before any game
code runs (mods load and patch here), and `Bootstrap.CreateHost` before the first scene (events, hotkeys,
asset swaps). Both JSON files are backed up as `.orig`.

## Mods folder

```
Mods/
  loader.json          reloadKey, dumpKey, textureRescanSeconds
  modlog.txt
  _dump/               F10 output (folders starting with _ or . are ignored)
  MyMod/
    mod.json           manifest (required)
    config.json        settings, created with defaults on first run
    MyMod.dll          code (optional: asset-only mods need no DLL)
    Textures/          PNG/JPG replacements, named after the texture
    Audio/             OGG/WAV/MP3 replacements, named after the clip
    Text/*.json        { "exact original text": "replacement" }
    Bundles/           AssetBundles, loaded with context.LoadAssetBundle("Bundles/x")
    Model/             your own data files (install.sh copies it; see Mods/Steve)
```

This is the game's `Mods` folder. `install.sh` copies only the DLLs and the `Text`, `Textures`, `Audio`,
`Bundles` and `Model` folders from this repo's `Mods/<name>` into it; any other folder is skipped. Add new data folders to its `for folder`
line.

`mod.json`:

```json
{
  "id": "mymod",
  "name": "My Mod",
  "version": "1.0.0",
  "dll": "MyMod.dll",
  "enabled": true,
  "dependsOn": ["offline"],
  "conflictsWith": []
}
```

Mods load in dependency order, then by folder name; later mods win when two replace the same asset.
A mod whose DLL throws during load is disabled and its Harmony patches are undone; mods that depend on it
are skipped. If two mods conflict, the one declaring the conflict is the one that doesn't load.

## Writing a code mod

Copy `Mods/Tweaks` (smallest example): a netstandard2.1 project referencing the loader; the shared
`Directory.Build.props` adds Harmony, Assembly-CSharp, UnityEngine.CoreModule and Newtonsoft.Json.

```csharp
public sealed class MyMod : IMod
{
    public void OnLoad(ModContext context)
    {
        int percent = context.Config.Get("sellValuePercent", 100);   // adds the key to config.json if missing
        GameHooks.AddSellValueModifier((value, category) => value * percent / 100);

        context.Harmony.Patch(AccessTools.Method(typeof(SomeGameClass), "SomeMethod"),
            prefix: new HarmonyMethod(typeof(MyMod).GetMethod(nameof(Prefix))));
        ModEvents.ConfigReloaded += () => percent = context.Config.Get("sellValuePercent", 100);
    }

    public static bool Prefix(ref int __result) { __result = 42; return false; }
}
```

- `context.Config`, `context.Log`, `context.Harmony` (owned by the mod), `context.Directory`, `context.LoadAssetBundle`.
- `GameHooks.AddSellValueModifier` (ore, ingot, fish, wood and crop sell values, including tooltips) and
  `GameHooks.AddXpModifier` (per skill), both after research bonuses.
- `ModEvents.Update`, `ModEvents.SceneLoaded`, `ModEvents.ConfigReloaded`.
- `Outfits.Register(id, name, icon)` adds a whole-character outfit to the top of the wardrobe; one outfit is
  worn at a time, kept on this PC, and synced to other players with the loader through the game's cosmetic
  sync. `Outfits.WornBy(chibiVisual)` says which outfit a player wears (see `Mods/Steve`, `Mods/Miku`).
- Decompiled game code (for finding methods to patch): `~/.cache/rea-work/arcadius/src/acs`.
- Assembly-CSharp has a global `JsonConvert` (a JsonUtility wrapper): write `Newtonsoft.Json.JsonConvert`.
- Use Newtonsoft rather than JsonUtility for your own data: mod DLLs load outside Unity's serializer.
- F9 reloads configs and assets, not code: after changing a DLL, restart the game. Installing while the
  game runs is fine (Proton doesn't lock the DLLs); the new code loads on the next start.
- Many game classes (`Chibi_Controller`, `Chibi_Animator`, `Networked_PlayerSetup`) derive from FishNet's
  `NetworkBehaviour`, so naming them in code needs a FishNet reference. Without one, look them up by name:
  `GetComponentInParent(AccessTools.TypeByName("Chibi_Controller"))`. `Chibi_Visual` is a plain
  MonoBehaviour and can be used directly.
- Extra Unity modules go in the mod's csproj, e.g. `UnityEngine.AnimationModule` (Animator) or
  `UnityEngine.ImageConversionModule` (`Texture2D.LoadImage`); see `Mods/Steve/Steve.csproj`.
- There's no LateUpdate event. To change transforms after the Animator has run (posing, attaching things
  to bones), use `Application.onBeforeRender`.
- The game runs under Proton: paths in the log look like `X:\git\Arcadius\Mods`, and file names are
  case-insensitive.

## Inspecting game assets

The decompiled C# shows behaviour, but prefabs, materials and Animator settings are only in
`Arcadius_Data/*.assets` (most gameplay prefabs are in `sharedassets0.assets`). Read them with UnityPy:

```
python3 -m venv ~/.cache/arcadius-venv && ~/.cache/arcadius-venv/bin/pip install UnityPy numpy
~/.cache/arcadius-venv/bin/python -I tools/dump_prefab.py ../Arcadius/Arcadius_Data Model_Chibi
```

`tools/dump_prefab.py` prints the whole tree around a named GameObject with local transforms and
components. Keep your own scripts outside the venv and don't name them after standard modules (an
`inspect.py` next to your script breaks Python itself).

## The player character

Players (and other characters built from the same rig) are "chibis" with a `Chibi_Visual`. The player
prefab (`sharedassets0.assets`):

```
Player                      CharacterController, Chibi_Controller, ...; renamed "Local Player" for you
  VisualChibi               Chibi_Visual
    Model_Chibi             Animator (cullingMode CullUpdateTransforms), origin at the feet, facing +Z
      ArmatureChibi         scale 100: bone offsets look ~100x too small
        hips > spine > chest > upper.chest > neck > head
            head > Head_Empty > hair_*, hat_*      MeshRenderers, one active at a time
            head > Model_Head, Model_Eyes          skin colour is Model_Head's material (URP Lit, _BaseColor)
          upper.chest > shoulder.L/R > upper.Arm.L/R > forearm.L/R > hand.L/R
            hand.R > Equip_RightHand               held tools are instantiated here
        hips > thigh.L/R > shin.L/R > foot.L/R
      top_*, low_*          SkinnedMeshRenderers (clothes, one of each active), hat_08_Skarf
```

- `.L` is the character's left, at -X. The bind pose is a T-pose (arms along ±X). Bones point down their
  local Y axis.
- Bind-pose heights in `Model_Chibi` space: hips 0.71, thighs 0.78, ankles 0.11, shoulders 1.24, neck 1.29,
  head 1.40, top of head about 1.7 m. Hands are 0.6 m out from the centre.
- The chibi stands with its knees bent, so its thighs aren't vertical when idle.
- The Animator stops updating bones when none of the chibi's renderers are visible. If you hide them, set
  `cullingMode = AnimatorCullingMode.AlwaysAnimate`, or the skeleton freezes.
- `Chibi_Visual` switches outfits with `SetActive` and recolours through `renderer.materials`, so
  `renderer.enabled = false` stays hidden through cosmetic changes.
- Your own player: `Networked_PlayerSetup.OnStartClient` renames it `"Local Player"` and stores it in the
  internal `Arcadius.Player.Runtime.activeChibi`.
- To get bind poses regardless of the current animation, read any `top_*`/`low_*` renderer:
  `model.worldToLocalMatrix * renderer.transform.localToWorldMatrix * renderer.sharedMesh.bindposes[i].inverse`
  is bone `renderer.bones[i]` in `Model_Chibi` space.

## Model mods without Unity

There's no Unity editor here, so AssetBundles can't be built. Blender is installed (`blender -b`), so:

1. Convert the model at build time with a Blender script into a simple file your mod reads (JSON of
   vertices, UVs and triangles). Convert to Unity's axes (Y up, +Z forward, left-handed) and reverse the
   triangle winding. Check the result before going in game, e.g. that the triangles face outward.
2. At runtime, build `Mesh` objects and give them a copy of a game material, e.g.
   `new Material(Model_Head's material)` with `mainTexture` set. `Shader.Find` may miss shaders the build
   stripped.
3. To animate, parent the meshes to the chibi's bones, or copy bone rotations each frame in
   `Application.onBeforeRender`.

`Mods/Steve` does all three: `convert_model.py`, `SteveResources.cs` and `SteveBody.cs`.

## Asset replacement

Press F10 in game, then look in `Mods/_dump`: `Textures/*.png` are named exactly as a replacement must
be; `audio.txt` lists clip names; `text.txt` lists UI strings as JSON keys. Only what the game has loaded
so far is dumped, so visit more screens and dump again.

- Textures are swapped in place on scene load and every `textureRescanSeconds` (default 5), so every
  material, sprite and UI image using them updates. Keep the original size: sprites in an atlas are cut from
  one shared texture.
- Audio is swapped when a sound plays.
- Text is swapped when the UI sets it, and on scene load for text stored in scenes.
- F9 picks up new and changed files. Removing a replacement needs a restart to restore the original.
- Models and prefabs: build an AssetBundle with Unity 6000.4 and load it from your mod's code, or, without
  Unity, see [Model mods without Unity](#model-mods-without-unity).

## Bundled mods

| Mod | Default | What it does |
|---|---|---|
| `Offline` | on | Play without Unity Gaming Services. Chat has no mining level 5 requirement and goes through the game server (local or private) instead of Vivox. The save is obfuscated so it can't easily be edited by hand (encrypted and signed with a fixed key in the mod, so not secure, but it can be copied to another computer); an edited save fails to load ("Save data failed."). Settings: `upkeepRequirementPercent` (10), `townBonuses` (true). |
| `Tweaks` | on | Example: `sellValuePercent` and `xpPercent` (100 = unchanged). Needs `offline`. |
| `PerfectHits` | on | Mining and woodcutting swings at or above `perfectHitPercent` (95) count as a 100% double-damage hit. Needs `offline`. |
| `TreeLogs` | on | Felled trees always drop `logsPerTree` logs (3) instead of 1 to 3. Needs `offline`. |
| `PrivateServer` | on | Brings back private servers: the server list gets **Host private server** (your world, open to others on UDP `hostPort`, default 7770, up to `maxPlayers`, 8), your saved `servers` (`[{"name": "Sam", "address": "1.2.3.4", "port": 7770}]`), and **Join by address...** (type `host:port`; remembered as `lastAddress`). `bindAddress` (empty = all) limits which network the host listens on. Every player needs `offline`. Friends outside your network need the port forwarded (UDP) or a VPN such as Tailscale. |
| `Steve` | on | Minecraft Steve as an outfit, driven by the game's animations: click **Steve** in the wardrobe to put it on or take it off. Other players with this mod see it too. Settings: `applyTo` (`outfit`, or `self`, `players` or `all` to force it on), `height` (1.7 m), `skin` (any 64x64 skin PNG, default `Model/steve.png`). The model and skin aren't included; see [Steve model and skin](#steve-model-and-skin). |
| `Miku` | on | Hatsune Miku as an outfit, the same way (a skinned model on its own skeleton, following the chibi's). Its hair (twin tails, fringe and side locks) swings with simple physics and stays out of the head, chest and hips. Settings: `applyTo` (as Steve), `height` (1.6 m), `hairPhysics` (true), `hairStiffness` (0.03, 0 = limp, 1 = rigid), `hairDamping` (0.4, air resistance), `hairGravity` (1). The model isn't included; see [Miku model](#miku-model). |
| `Supra` | on | A gag outfit: you become a Toyota Supra (flat colours, rolling wheels, the body tilts when speeding up, braking and turning; held tools are hidden). Settings: `applyTo` (as Steve), `length` (3 m). The model isn't included; see [Supra model](#supra-model). |
| `CloudCodeCapture` | off | Developer tool: plays online and logs Cloud Code traffic to `cloudcode_capture.jsonl`. Disable `offline` to use it. |

Gameplay tweaks are only meant for offline play; online, the server is authoritative.

### Steve model and skin

The Steve model and skin are third-party assets (Steve is Mojang's), so this repo doesn't include them.
Before `./install.sh`, add them to `Mods/Steve` (both folders are git-ignored):

1. A Steve FBX made of six boxes in the standard Minecraft proportions (head, body, two arms, two legs) with
   UVs for a 64x64 skin, at `minecraft-steve/source/steve.fbx`.
2. Convert it:
   `blender -b --python convert_model.py -- minecraft-steve/source/steve.fbx Model/steve.json`.
   The script checks for six boxes and prints each part's bounds.
3. A 64x64 Minecraft skin (yours, or Steve's from the game) at `Model/steve.png`.

### Miku model

The Miku model is a third-party fan model, so this repo doesn't include it. `Mods/Miku/convert_model.py`
converts any rigged MMD model with the standard bones (センター, 上半身, 首, 頭, 肩, 腕, ひじ, 手首, 足, ひざ, 足首),
given as an FBX exported by mmd_tools plus its PMX (for the material textures). Before `./install.sh`, in
`Mods/Miku` (both folders are git-ignored):

1. Put the FBX, PMX and textures in `hatsune-miku/`.
2. Convert it:
   `blender -b --python convert_model.py -- "hatsune-miku/PS - Hatsune Miku.fbx" "hatsune-miku/PS - Hatsune Miku.pmx" Model`.
   This writes `Model/miku.json`, copies the textures and renders `Model/icon.png` for the wardrobe. It
   prints each bone's joint, the materials and how much weight each bone carries.

### Supra model

The Toyota Supra is a free third-party Blender model, so this repo doesn't include it. Before `./install.sh`,
in `Mods/Supra` (both folders are git-ignored):

1. Put the `.blend` in `toyota-supra/`.
2. Convert it: `blender -b "toyota-supra/supra free.blend" --python convert_model.py -- Model`.
   This drops hidden parts (brakes, suspension, engine bay), simplifies the wheels, gives each material a flat
   colour (`COLOURS` in the script; the paint is orange) and renders `Model/icon.png`.
