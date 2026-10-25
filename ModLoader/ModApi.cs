using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcadiusModLoader
{
	// Entry point of a code mod. The loader creates one instance of each IMod class in the mod's DLL
	// and calls OnLoad before the first scene loads, so patches are in place before game code runs.
	public interface IMod
	{
		void OnLoad(ModContext context);
	}

	// mod.json
	public sealed class ModManifest
	{
		public string id;

		public string name;

		public string version = "1.0.0";

		public string author;

		public string description;

		// DLL in the mod folder holding IMod classes; omit for asset-only mods.
		public string dll;

		public bool enabled = true;

		// Ids of mods that must load first.
		public List<string> dependsOn = new List<string>();

		// Ids of mods that can't run alongside this one.
		public List<string> conflictsWith = new List<string>();
	}

	public sealed class ModContext
	{
		private readonly Dictionary<string, AssetBundle> _bundles = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);

		internal ModContext(ModManifest manifest, string directory)
		{
			Manifest = manifest;
			Directory = directory;
			Log = new ModLog(manifest.id);
			Config = new ModConfig(Path.Combine(directory, "config.json"), Log);
			Harmony = new Harmony("arcadius.mod." + manifest.id);
		}

		public ModManifest Manifest { get; }

		// The mod's folder (Mods/<folder>).
		public string Directory { get; }

		public ModLog Log { get; }

		public ModConfig Config { get; }

		// Harmony instance owned by this mod; its patches are undone if OnLoad throws.
		public Harmony Harmony { get; }

		// Loads an AssetBundle from the mod folder (cached), e.g. LoadAssetBundle("Bundles/models").
		public AssetBundle LoadAssetBundle(string relativePath)
		{
			if (!_bundles.TryGetValue(relativePath, out AssetBundle bundle) || bundle == null)
			{
				bundle = AssetBundle.LoadFromFile(Path.Combine(Directory, relativePath));
				if (bundle == null)
				{
					Log.Error("Could not load AssetBundle " + relativePath);
				}
				_bundles[relativePath] = bundle;
			}
			return bundle;
		}
	}

	// Game-wide events for mods. Handlers that throw are logged and skipped.
	public static class ModEvents
	{
		private static readonly ModLog Log = new ModLog("ModLoader");

		// Every frame.
		public static event Action Update;

		public static event Action<Scene> SceneLoaded;

		// After the reload hotkey re-read every config.json.
		public static event Action ConfigReloaded;

		internal static void RaiseUpdate()
		{
			Raise(Update, h => ((Action)h)());
		}

		internal static void RaiseSceneLoaded(Scene scene)
		{
			Raise(SceneLoaded, h => ((Action<Scene>)h)(scene));
		}

		internal static void RaiseConfigReloaded()
		{
			Raise(ConfigReloaded, h => ((Action)h)());
		}

		private static void Raise(Delegate handlers, Action<Delegate> invoke)
		{
			if (handlers == null)
			{
				return;
			}
			foreach (Delegate handler in handlers.GetInvocationList())
			{
				try
				{
					invoke(handler);
				}
				catch (Exception ex)
				{
					Log.Error("Event handler " + handler.Method.DeclaringType + "." + handler.Method.Name + " failed.", ex);
				}
			}
		}
	}
}
