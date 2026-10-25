using ArcadiusModLoader;
using HarmonyLib;
using UnityEngine;

namespace ArcadiusTreeLogs
{
	// A felled tree spawns one falling Log, which breaks into Random.Range(1, 4) wood pickups after 3 seconds
	// (the tree's own extra yield is always 0). This sets that count from config.json instead.
	public sealed class TreeLogsEntry : IMod
	{
		private static readonly AccessTools.FieldRef<Log, int> YieldCount = AccessTools.FieldRefAccess<Log, int>("yieldCount");

		private static ModConfig _config;

		private static int _logsPerTree;

		public void OnLoad(ModContext context)
		{
			_config = context.Config;
			ReadSettings();
			ModEvents.ConfigReloaded += ReadSettings;
			context.Harmony.Patch(AccessTools.Method(typeof(Log), "Start"),
				postfix: new HarmonyMethod(typeof(TreeLogsEntry), nameof(AfterLogStart)));
		}

		private static void ReadSettings()
		{
			_logsPerTree = Mathf.Clamp(_config.Get("logsPerTree", 3), 0, 100);
		}

		public static void AfterLogStart(Log __instance)
		{
			YieldCount(__instance) = _logsPerTree;
		}
	}
}
