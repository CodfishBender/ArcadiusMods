using ArcadiusModLoader;
using HarmonyLib;
using TrilliumStudio;

namespace ArcadiusGraphicsSettings
{
	// More display and graphics settings on the game's Settings page. The game only has quality, resolution,
	// fullscreen, sensitivity and volume, and caps the frame rate at 60 (GameManager.Awake).
	public sealed class GraphicsSettingsEntry : IMod
	{
		internal static ModContext Context;

		public void OnLoad(ModContext context)
		{
			Context = context;
			Harmony harmony = context.Harmony;
			// The game sets the 60 FPS cap here.
			harmony.Patch(AccessTools.Method(typeof(GameManager), "Awake"),
				postfix: new HarmonyMethod(typeof(GraphicsSettingsEntry), nameof(Reapply)));
			// Changing the quality level resets VSync and the level of detail, and can switch pipeline assets.
			harmony.Patch(AccessTools.Method(typeof(SettingsHandler), nameof(SettingsHandler.ChangeQuality)),
				postfix: new HarmonyMethod(typeof(GraphicsSettingsEntry), nameof(Reapply)));
			harmony.Patch(AccessTools.Method(typeof(SettingsHandler), "Start"),
				postfix: new HarmonyMethod(typeof(GraphicsSettingsEntry), nameof(AfterSettingsStart)));
			// UserInterfaceBehaviour is a NetworkBehaviour, so it's named rather than referenced.
			harmony.Patch(AccessTools.Method("UserInterfaceBehaviour:OpenSettings"),
				postfix: new HarmonyMethod(typeof(GraphicsSettingsEntry), nameof(AfterOpenSettings)));

			ModEvents.SceneLoaded += _ => GraphicsOptions.Apply();
			ModEvents.Update += () =>
			{
				GraphicsOptions.Update();
				FpsCounter.Update();
			};
			ModEvents.ConfigReloaded += () =>
			{
				GraphicsOptions.Read();
				GraphicsOptions.Apply();
				SettingsMenu.Refresh();
			};
		}

		public static void Reapply()
		{
			GraphicsOptions.Apply();
		}

		public static void AfterSettingsStart(SettingsHandler __instance)
		{
			GraphicsOptions.Apply();
			SettingsMenu.Build(__instance);
		}

		public static void AfterOpenSettings()
		{
			SettingsMenu.Refresh();
			SettingsMenu.ScrollToTop();
		}
	}
}
