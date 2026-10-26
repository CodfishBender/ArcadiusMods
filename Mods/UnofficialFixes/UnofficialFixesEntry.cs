using ArcadiusModLoader;
using HarmonyLib;
using UnityEngine;

namespace ArcadiusUnofficialFixes
{
	// Fixes for things the game gets wrong on its own, unrelated to any feature mod. Each fix has a switch in
	// config.json, read again on F9, so one that misbehaves after a game update can be turned off alone.
	// Every fix is patched in at load and checks its switch when it runs.
	public sealed class UnofficialFixesEntry : IMod
	{
		internal static ModContext Context;

		internal static bool RememberQuality;

		internal static bool RememberDisplayMode;

		internal static bool FixF11Fullscreen;

		internal static bool NoHotbarScrollInSettings;

		internal static bool HoldToJump;

		internal static bool LogFriction;

		internal static float LogDrag;

		internal static float LogRollingDrag;

		public void OnLoad(ModContext context)
		{
			Context = context;
			ReadSettings();
			ModEvents.ConfigReloaded += ReadSettings;
			Harmony harmony = context.Harmony;
			QualityFix.Patch(harmony);
			DisplayModeFix.Patch(harmony);
			F11Fix.Patch(harmony);
			HotbarFix.Patch(harmony);
			JumpFix.Patch(harmony);
			LogFrictionFix.Patch(harmony);
		}

		private static void ReadSettings()
		{
			ModConfig config = Context.Config;
			RememberQuality = config.Get("rememberQuality", true);
			RememberDisplayMode = config.Get("rememberDisplayMode", true);
			FixF11Fullscreen = config.Get("fixF11Fullscreen", true);
			NoHotbarScrollInSettings = config.Get("noHotbarScrollInSettings", true);
			HoldToJump = config.Get("holdToJump", true);
			LogFriction = config.Get("logFriction", true);
			LogDrag = Mathf.Clamp(config.Get("logDrag", 0.5f), 0f, 20f);
			LogRollingDrag = Mathf.Clamp(config.Get("logRollingDrag", 3f), 0f, 50f);
		}
	}
}
