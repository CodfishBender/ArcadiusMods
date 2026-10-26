using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Arcadius.Player;
using ArcadiusModLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ArcadiusGraphicsSettings
{
	// The settings, stored in config.json and applied to Unity. A setting missing from config.json starts at
	// the game's own value, so a fresh install changes nothing except what the player picks.
	internal static class GraphicsOptions
	{
		public static readonly int[] FpsLimits = { 30, 60, 75, 90, 120, 144, 165, 240, 0 };

		public static readonly string[] AntiAliasingNames = { "Off", "FXAA", "SMAA", "TAA", "MSAA 2x", "MSAA 4x", "MSAA 8x" };

		public static readonly string[] UpscalingNames = { "Auto", "Bilinear", "Nearest", "FSR 1.0" };

		private static readonly UpscalingFilterSelection[] UpscalingFilters =
			{ UpscalingFilterSelection.Auto, UpscalingFilterSelection.Linear, UpscalingFilterSelection.Point, UpscalingFilterSelection.FSR };

		public static readonly string[] ShadowNames = { "Off", "Low", "Medium", "High", "Very high" };

		private static readonly int[] ShadowResolutions = { 0, 512, 1024, 2048, 4096 };

		public static readonly string[] TextureNames = { "Full", "Half", "Quarter" };

		public static readonly string[] AnisotropicNames = { "Off", "Per texture", "Forced on" };

		public static readonly string[] CornerNames = { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };

		private static ModConfig Config => GraphicsSettingsEntry.Context.Config;

		private static ModLog Log => GraphicsSettingsEntry.Context.Log;

		// Unity's own settings, read before the mod changes anything.
		private static bool _captured;

		private static float _gameLodBias = 2f;

		private static int _gameAnisotropic = (int)AnisotropicFiltering.ForceEnable;

		private static int _gameTextureLimit;

		private static bool _gameVSync;

		private static float _gameRenderScale = 1f;

		private static UpscalingFilterSelection _gameUpscaling = UpscalingFilterSelection.Auto;

		private static float _gameShadowDistance = 40f;

		private static int _gameShadowResolution = 2048;

		private static bool _gameMainLightShadows = true;

		private static int _gameMsaa = 1;

		private static bool? _gameAmbientOcclusion;

		// The player's camera and what the game set on it.
		private static Camera _camera;

		private static float _gameFieldOfView = 60f;

		private static AntialiasingMode _gameCameraAntiAliasing;

		private static bool _gameAllowMsaa;

		private static Volume _volume;

		private static Bloom _bloom;

		private static Vignette _vignette;

		private static UniversalRenderPipelineAsset _appliedPipeline;

		private static readonly HashSet<UniversalRenderPipelineAsset> Warned = new HashSet<UniversalRenderPipelineAsset>();

		public static int FpsLimit;

		public static bool VSync;

		public static int FieldOfView;

		public static int RenderScale;

		public static int Upscaling;

		public static int AntiAliasing;

		public static int Shadows;

		public static int ShadowDistance;

		public static int Textures;

		public static int Anisotropic;

		public static int DetailDistance;

		public static bool Bloom;

		public static bool Vignette;

		public static bool AmbientOcclusion;

		public static bool ShowFps;

		public static int FpsCorner;

		private static bool _read;

		public static UniversalRenderPipelineAsset Pipeline => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

		public static bool HasAmbientOcclusion => AmbientOcclusionFeatures(Pipeline).Count > 0;

		public static bool CameraFound => _camera != null;

		private static void Capture()
		{
			if (_captured)
			{
				return;
			}
			_captured = true;
			_gameLodBias = QualitySettings.lodBias;
			_gameAnisotropic = (int)QualitySettings.anisotropicFiltering;
			_gameTextureLimit = QualitySettings.globalTextureMipmapLimit;
			_gameVSync = QualitySettings.vSyncCount > 0;
			UniversalRenderPipelineAsset asset = Pipeline;
			if (asset == null)
			{
				Log.Warning("The game isn't using URP (" + GraphicsSettings.currentRenderPipeline + "); render scale, shadows, anti-aliasing and ambient occlusion won't change anything.");
				return;
			}
			_gameRenderScale = asset.renderScale;
			_gameUpscaling = asset.upscalingFilter;
			_gameShadowDistance = asset.shadowDistance;
			_gameShadowResolution = asset.mainLightShadowmapResolution;
			_gameMainLightShadows = asset.supportsMainLightShadows;
			_gameMsaa = asset.msaaSampleCount;
			List<ScriptableRendererFeature> ao = AmbientOcclusionFeatures(asset);
			if (ao.Count > 0)
			{
				_gameAmbientOcclusion = ao[0].isActive;
			}
		}

		public static void Read()
		{
			Capture();
			_read = true;
			FpsLimit = Math.Max(0, Config.Get("fpsLimit", 60));
			VSync = Config.Get("vSync", _gameVSync);
			RenderScale = Mathf.Clamp(Config.Get("renderScalePercent", Mathf.RoundToInt(_gameRenderScale * 100f)), 25, 200);
			Upscaling = Pick(UpscalingNames, Config.Get("upscaling", UpscalingNames[Math.Max(0, Array.IndexOf(UpscalingFilters, _gameUpscaling))]), 0);
			Shadows = Pick(ShadowNames, Config.Get("shadows", ShadowNames[GameShadowQuality()]), GameShadowQuality());
			ShadowDistance = Mathf.Clamp(Config.Get("shadowDistance", Mathf.RoundToInt(_gameShadowDistance)), 5, 500);
			Textures = Pick(TextureNames, Config.Get("textureQuality", TextureNames[Mathf.Clamp(_gameTextureLimit, 0, 2)]), 0);
			Anisotropic = Pick(AnisotropicNames, Config.Get("anisotropicFiltering", AnisotropicNames[Mathf.Clamp(_gameAnisotropic, 0, 2)]), 2);
			DetailDistance = Mathf.Clamp(Config.Get("detailDistancePercent", 100), 25, 400);
			Bloom = Config.Get("bloom", true);
			Vignette = Config.Get("vignette", true);
			AmbientOcclusion = Config.Get("ambientOcclusion", _gameAmbientOcclusion ?? true);
			ShowFps = Config.Get("showFps", false);
			FpsCorner = Pick(CornerNames, Config.Get("fpsCounterCorner", "TopLeft"), 0);
			ReadCameraSettings();
		}

		// The camera's settings default to what the game gave the camera, so they're read once it exists.
		private static void ReadCameraSettings()
		{
			if (_camera == null)
			{
				return;
			}
			FieldOfView = Mathf.Clamp(Config.Get("fieldOfView", Mathf.RoundToInt(_gameFieldOfView)), 30, 120);
			AntiAliasing = Pick(AntiAliasingNames, Config.Get("antiAliasing", AntiAliasingNames[GameAntiAliasing()]), GameAntiAliasing());
		}

		private static int Pick(string[] names, string value, int fallback)
		{
			int index = Array.FindIndex(names, n => string.Equals(n, value?.Trim(), StringComparison.OrdinalIgnoreCase));
			return index >= 0 ? index : fallback;
		}

		private static int GameShadowQuality()
		{
			if (!_gameMainLightShadows)
			{
				return 0;
			}
			int index = Array.IndexOf(ShadowResolutions, _gameShadowResolution);
			return index > 0 ? index : 3;
		}

		private static int GameAntiAliasing()
		{
			if (_gameAllowMsaa && _gameMsaa > 1)
			{
				return _gameMsaa >= 8 ? 6 : _gameMsaa >= 4 ? 5 : 4;
			}
			switch (_gameCameraAntiAliasing)
			{
				case AntialiasingMode.FastApproximateAntialiasing:
					return 1;
				case AntialiasingMode.SubpixelMorphologicalAntiAliasing:
					return 2;
				case AntialiasingMode.TemporalAntiAliasing:
					return 3;
				default:
					return 0;
			}
		}

		// Stores a setting the player changed in the menu and applies everything.
		public static void Save<T>(string key, T value)
		{
			Config.Set(key, value);
			Apply();
		}

		public static void Apply()
		{
			if (!_read)
			{
				Read();
			}
			// VSync wins over the frame rate limit in Unity.
			if (QualitySettings.vSyncCount != (VSync ? 1 : 0))
			{
				QualitySettings.vSyncCount = VSync ? 1 : 0;
			}
			Application.targetFrameRate = FpsLimit > 0 ? FpsLimit : -1;
			// Only when changed: a new texture limit reloads every texture.
			if (QualitySettings.globalTextureMipmapLimit != Textures)
			{
				QualitySettings.globalTextureMipmapLimit = Textures;
			}
			if (QualitySettings.anisotropicFiltering != (AnisotropicFiltering)Anisotropic)
			{
				QualitySettings.anisotropicFiltering = (AnisotropicFiltering)Anisotropic;
			}
			QualitySettings.lodBias = _gameLodBias * DetailDistance / 100f;
			ApplyPipeline(Pipeline);
			ApplyCamera();
			ApplyVolume();
			FpsCounter.SetVisible(ShowFps, FpsCorner);
			if (!_logged)
			{
				_logged = true;
				Log.Info("Applied the saved settings: FPS limit " + (FpsLimit > 0 ? FpsLimit.ToString() : "unlimited") + ", VSync " + (VSync ? "on" : "off") +
					", render scale " + RenderScale + "%, shadows " + ShadowNames[Shadows] + " to " + ShadowDistance + " m, textures " + TextureNames[Textures] +
					" (pipeline " + (Pipeline != null ? Pipeline.name : "none") + ").");
			}
		}

		private static bool _logged;

		private static void ApplyPipeline(UniversalRenderPipelineAsset asset)
		{
			if (asset == null)
			{
				return;
			}
			_appliedPipeline = asset;
			asset.renderScale = RenderScale / 100f;
			asset.upscalingFilter = UpscalingFilters[Upscaling];
			asset.shadowDistance = ShadowDistance;
			if (Shadows > 0)
			{
				asset.mainLightShadowmapResolution = ShadowResolutions[Shadows];
			}
			SetMainLightShadows(asset, Shadows > 0);
			// The anti-aliasing setting is read with the camera's.
			if (CameraFound)
			{
				asset.msaaSampleCount = AntiAliasing >= 4 ? 1 << (AntiAliasing - 3) : 1;
			}
			foreach (ScriptableRendererFeature feature in AmbientOcclusionFeatures(asset))
			{
				feature.SetActive(AmbientOcclusion);
			}
		}

		// supportsMainLightShadows has no public setter.
		private static readonly FieldInfo MainLightShadowsField = AccessTools.Field(typeof(UniversalRenderPipelineAsset), "m_MainLightShadowsSupported");

		private static void SetMainLightShadows(UniversalRenderPipelineAsset asset, bool on)
		{
			if (MainLightShadowsField != null)
			{
				MainLightShadowsField.SetValue(asset, on);
			}
			else if (Warned.Add(asset))
			{
				Log.Warning("UniversalRenderPipelineAsset.m_MainLightShadowsSupported is missing; shadows can't be turned off, only set to Low.");
			}
		}

		// The renderer features of every renderer the asset uses whose type is URP's SSAO.
		private static List<ScriptableRendererFeature> AmbientOcclusionFeatures(UniversalRenderPipelineAsset asset)
		{
			List<ScriptableRendererFeature> found = new List<ScriptableRendererFeature>();
			if (asset == null || !(Traverse.Create(asset).Field("m_RendererDataList").GetValue() is IEnumerable renderers))
			{
				return found;
			}
			foreach (object renderer in renderers)
			{
				if (renderer is ScriptableRendererData data)
				{
					foreach (ScriptableRendererFeature feature in data.rendererFeatures)
					{
						if (feature != null && feature.GetType().Name == "ScreenSpaceAmbientOcclusion")
						{
							found.Add(feature);
						}
					}
				}
			}
			return found;
		}

		// Every frame: keeps the camera's field of view. Every second: finds the player's camera when it appears,
		// and applies the settings again if the game changed one of them back.
		public static void Update()
		{
			if (_camera != null && !Mathf.Approximately(_camera.fieldOfView, FieldOfView))
			{
				_camera.fieldOfView = FieldOfView;
			}
			if (!_read || Time.unscaledTime < _nextCheck)
			{
				return;
			}
			_nextCheck = Time.unscaledTime + 1f;
			if (_camera == null && FindCamera())
			{
				return;
			}
			string changed = Changed();
			if (changed != null)
			{
				// Once per setting, so a game that keeps resetting one doesn't flood the log.
				if (Reported.Add(changed))
				{
					Log.Info("The game changed " + changed + "; applying the saved settings again.");
				}
				Apply();
			}
		}

		private static float _nextCheck;

		private static readonly HashSet<string> Reported = new HashSet<string>();

		// The first of the applied settings that no longer holds, or null.
		private static string Changed()
		{
			if (QualitySettings.vSyncCount != (VSync ? 1 : 0))
			{
				return "VSync";
			}
			if (Application.targetFrameRate != (FpsLimit > 0 ? FpsLimit : -1))
			{
				return "the frame rate limit (" + Application.targetFrameRate + ")";
			}
			if (QualitySettings.globalTextureMipmapLimit != Textures)
			{
				return "the texture quality";
			}
			if (QualitySettings.anisotropicFiltering != (AnisotropicFiltering)Anisotropic)
			{
				return "anisotropic filtering";
			}
			if (!Mathf.Approximately(QualitySettings.lodBias, _gameLodBias * DetailDistance / 100f))
			{
				return "the detail distance";
			}
			UniversalRenderPipelineAsset asset = Pipeline;
			if (asset != null)
			{
				if (asset != _appliedPipeline)
				{
					return "the render pipeline asset (" + asset.name + ")";
				}
				if (!Mathf.Approximately(asset.renderScale, RenderScale / 100f))
				{
					return "the render scale";
				}
				if (asset.upscalingFilter != UpscalingFilters[Upscaling])
				{
					return "the upscaling filter";
				}
				if (!Mathf.Approximately(asset.shadowDistance, ShadowDistance))
				{
					return "the shadow distance";
				}
				if ((MainLightShadowsField != null && asset.supportsMainLightShadows != Shadows > 0) ||(Shadows > 0 && asset.mainLightShadowmapResolution != ShadowResolutions[Shadows]))
				{
					return "the shadow quality";
				}
				if (CameraFound && asset.msaaSampleCount != (AntiAliasing >= 4 ? 1 << (AntiAliasing - 3) : 1))
				{
					return "MSAA";
				}
			}
			if (_camera != null && (_camera.allowMSAA != AntiAliasing >= 4 || CameraAntiAliasing(_camera) != AntiAliasingMode(AntiAliasing)))
			{
				return "the camera's anti-aliasing";
			}
			return null;
		}

		private static AntialiasingMode? CameraAntiAliasing(Camera camera)
		{
			UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
			return data != null ? data.antialiasing : (AntialiasingMode?)null;
		}

		private static AntialiasingMode AntiAliasingMode(int setting)
		{
			return setting switch
			{
				1 => AntialiasingMode.FastApproximateAntialiasing,
				2 => AntialiasingMode.SubpixelMorphologicalAntiAliasing,
				3 => AntialiasingMode.TemporalAntiAliasing,
				_ => AntialiasingMode.None
			};
		}

		private static bool FindCamera()
		{
			Player_Camera player = UnityEngine.Object.FindAnyObjectByType<Player_Camera>();
			Camera camera = player != null ? Traverse.Create(player).Field("m_Camera").GetValue<Camera>() : null;
			if (camera == null)
			{
				return false;
			}
			_camera = camera;
			_gameFieldOfView = camera.fieldOfView;
			_gameAllowMsaa = camera.allowMSAA;
			UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
			_gameCameraAntiAliasing = data != null ? data.antialiasing : AntialiasingMode.None;
			ReadCameraSettings();
			// Everything, as MSAA on the pipeline asset waits for the camera's anti-aliasing setting.
			Apply();
			SettingsMenu.Refresh();
			Log.Info("Applied to the player's camera: field of view " + FieldOfView + ", anti-aliasing " + AntiAliasingNames[AntiAliasing] + ".");
			return true;
		}

		private static void ApplyCamera()
		{
			if (_camera == null)
			{
				return;
			}
			_camera.fieldOfView = FieldOfView;
			_camera.allowMSAA = AntiAliasing >= 4;
			UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
			if (data != null)
			{
				data.antialiasing = AntiAliasingMode(AntiAliasing);
				data.antialiasingQuality = AntialiasingQuality.High;
			}
		}

		// Turning bloom or the vignette off is a global volume above the scene's that sets their intensity to 0.
		private static void ApplyVolume()
		{
			if (Bloom && Vignette && _volume == null)
			{
				return;
			}
			if (_volume == null)
			{
				Volume scene = UnityEngine.Object.FindAnyObjectByType<Volume>();
				GameObject go = new GameObject("GraphicsSettings Volume");
				if (scene != null)
				{
					// The camera only blends volumes on the layers in its volume mask.
					go.layer = scene.gameObject.layer;
					go.transform.SetParent(scene.transform, false);
				}
				else
				{
					UnityEngine.Object.DontDestroyOnLoad(go);
				}
				_volume = go.AddComponent<Volume>();
				_volume.isGlobal = true;
				_volume.priority = 1000f;
				VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
				_bloom = profile.Add<Bloom>();
				_bloom.intensity.Override(0f);
				_vignette = profile.Add<Vignette>();
				_vignette.intensity.Override(0f);
				_volume.sharedProfile = profile;
			}
			_bloom.active = !Bloom;
			_vignette.active = !Vignette;
		}
	}
}
