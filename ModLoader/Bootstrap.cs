using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ArcadiusModLoader
{
	// Started by Unity through entries in Arcadius_Data/RuntimeInitializeOnLoads.json (added by install.sh):
	//   Init        AfterAssembliesLoaded: load mods and apply their patches before any game code runs.
	//   CreateHost  BeforeSceneLoad: create the object that drives events, hotkeys and asset swaps.
	public static class Bootstrap
	{
		private static readonly ModLog Log = new ModLog("ModLoader");

		private static bool _initialized;

		internal static ModConfig LoaderConfig;

		public static void Init()
		{
			if (_initialized)
			{
				return;
			}
			_initialized = true;
			try
			{
				string modsDirectory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Mods");
				Directory.CreateDirectory(modsDirectory);
				ModLog.StartFile(Path.Combine(modsDirectory, "modlog.txt"));
				Log.Info("Arcadius mod loader " + typeof(Bootstrap).Assembly.GetName().Version + ", mods folder: " + modsDirectory);
				LoaderConfig = new ModConfig(Path.Combine(modsDirectory, "loader.json"), Log);
				ModManager.LoadAll(modsDirectory);
				RebuildAssets();
			}
			catch (Exception ex)
			{
				Log.Error("Mod loader failed to start.", ex);
			}
		}

		public static void CreateHost()
		{
			if (!_initialized)
			{
				Init();
			}
			GameObject host = new GameObject("ArcadiusModLoader");
			host.hideFlags = HideFlags.HideAndDontSave;
			UnityEngine.Object.DontDestroyOnLoad(host);
			host.AddComponent<LoaderBehaviour>();
		}

		internal static void RebuildAssets()
		{
			TextureSwaps.Rebuild(ModManager.LoadedDirectories);
			AudioSwaps.Rebuild(ModManager.LoadedDirectories);
			TextSwaps.Rebuild(ModManager.LoadedDirectories);
		}
	}

	internal sealed class LoaderBehaviour : MonoBehaviour
	{
		private static readonly ModLog Log = new ModLog("ModLoader");

		private Key _reloadKey;

		private Key _dumpKey;

		private float _rescanSeconds;

		private float _nextRescan;

		private void Awake()
		{
			ReadSettings();
			SceneManager.sceneLoaded += OnSceneLoaded;
			StartCoroutine(AudioSwaps.Load());
		}

		private void OnDestroy()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
		}

		private void ReadSettings()
		{
			ModConfig config = Bootstrap.LoaderConfig;
			_reloadKey = ParseKey(config.Get("reloadKey", "F9"), Key.F9);
			_dumpKey = ParseKey(config.Get("dumpKey", "F10"), Key.F10);
			// Textures loaded after a scene starts (e.g. opened UI) are picked up by this periodic rescan.
			_rescanSeconds = Mathf.Max(0f, config.Get("textureRescanSeconds", 5f));
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			ModEvents.RaiseSceneLoaded(scene);
			StartCoroutine(ApplyNextFrame());
		}

		private IEnumerator ApplyNextFrame()
		{
			yield return null;
			ApplyAssets();
		}

		private static void ApplyAssets()
		{
			TextureSwaps.Apply();
			AudioSwaps.ApplyToSources();
			TextSwaps.ApplyToLoadedText();
		}

		private void Update()
		{
			ModEvents.RaiseUpdate();
			Keyboard keyboard = Keyboard.current;
			if (keyboard != null)
			{
				if (keyboard[_reloadKey].wasPressedThisFrame)
				{
					Reload();
				}
				if (keyboard[_dumpKey].wasPressedThisFrame && !AssetDumper.Running)
				{
					StartCoroutine(AssetDumper.Dump(Path.Combine(ModManager.ModsDirectory, "_dump")));
				}
			}
			if (_rescanSeconds > 0f && Time.unscaledTime >= _nextRescan)
			{
				_nextRescan = Time.unscaledTime + _rescanSeconds;
				TextureSwaps.Apply();
			}
		}

		// Reload hotkey: re-read every config.json and asset folder. Code changes need a restart.
		private void Reload()
		{
			Bootstrap.LoaderConfig.Reload();
			ReadSettings();
			foreach (ModInfo mod in ModManager.All)
			{
				mod.Context?.Config.Reload();
			}
			ModEvents.RaiseConfigReloaded();
			Bootstrap.RebuildAssets();
			StartCoroutine(AudioSwaps.Load());
			ApplyAssets();
			Log.Info("Reloaded configs and assets (" + TextureSwaps.Count + " textures, " + AudioSwaps.Count + " audio, " + TextSwaps.Count + " text).");
		}

		private static Key ParseKey(string name, Key fallback)
		{
			if (Enum.TryParse(name, true, out Key key) && key != Key.None)
			{
				return key;
			}
			Log.Warning("Unknown key '" + name + "' in loader.json; using " + fallback + ".");
			return fallback;
		}
	}
}
