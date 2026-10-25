using ArcadiusModLoader;
using HarmonyLib;
using UnityEngine;

namespace ArcadiusPerfectHits
{
	// Chibi_Gathering's PreSwing_Mining and PreSwing_WoodCutting treat a swing power of 99.5% or more as a
	// double-damage hit and round it to 100%. Rounding up from a lower threshold first makes those hits count;
	// the mining request then carries 100%, so a host's server check agrees.
	public sealed class PerfectHitsEntry : IMod
	{
		private static readonly AccessTools.FieldRef<Chibi_Gathering, float> SwingPower = AccessTools.FieldRefAccess<Chibi_Gathering, float>("amtPower");

		private static ModConfig _config;

		private static float _perfectHitPercent;

		public void OnLoad(ModContext context)
		{
			_config = context.Config;
			ReadSettings();
			ModEvents.ConfigReloaded += ReadSettings;
			HarmonyMethod prefix = new HarmonyMethod(typeof(PerfectHitsEntry), nameof(BeforeSwing));
			context.Harmony.Patch(AccessTools.Method(typeof(Chibi_Gathering), "PreSwing_Mining"), prefix: prefix);
			context.Harmony.Patch(AccessTools.Method(typeof(Chibi_Gathering), "PreSwing_WoodCutting"), prefix: prefix);
			// The charging bar (only mining and woodcutting draw it) shows those swings full.
			context.Harmony.Patch(AccessTools.Method(typeof(GatheringUI), nameof(GatheringUI.SetSwingPowerBar)),
				prefix: new HarmonyMethod(typeof(PerfectHitsEntry), nameof(BeforeDrawBar)));
		}

		private static void ReadSettings()
		{
			_perfectHitPercent = Mathf.Clamp(_config.Get("perfectHitPercent", 95f), 0f, 100f);
		}

		public static void BeforeSwing(Chibi_Gathering __instance)
		{
			ref float power = ref SwingPower(__instance);
			if (IsPerfect(power))
			{
				power = 100f;
			}
		}

		public static void BeforeDrawBar(ref float swingPower)
		{
			if (IsPerfect(swingPower))
			{
				swingPower = 100f;
			}
		}

		// Rounded like the bar's value, so a swing counts exactly when the bar shows it full.
		private static bool IsPerfect(float power)
		{
			return Mathf.Round(power) >= _perfectHitPercent;
		}
	}
}
