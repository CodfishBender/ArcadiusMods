using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using ArcadiusModLoader;
using HarmonyLib;
using Unity.Services.CloudCode;
using UnityEngine;

namespace ArcadiusOffline
{
	// Settings from Mods/Offline/config.json (re-read with the reload hotkey).
	public static class OfflineSettings
	{
		// Upkeep requirements as a percentage of the server's community-wide amounts. 100 = server amounts.
		public static int UpkeepRequirementPercent = 10;

		// Apply the town upkeep sell and Fishing XP bonuses (the online game only displays them).
		public static bool TownBonuses = true;
	}

	// Replaces Unity Gaming Services with local stand-ins and hosts the game world in-process.
	public sealed class OfflineEntry : IMod
	{
		private ModConfig _config;

		public void OnLoad(ModContext context)
		{
			_config = context.Config;
			ReadSettings();
			ModEvents.ConfigReloaded += ReadSettings;
			Harmony harmony = context.Harmony;

			// Unity Gaming Services: no sign-in, local saves, stubbed Cloud Code.
			// IsSignedIn stays false so account linking, Vivox chat and UGS name lookups stay idle.
			Prefix(harmony, typeof(UnityServicesGateway), nameof(UnityServicesGateway.InitializeAndSignInAsync), nameof(Patches.CompletedTask));
			Prefix(harmony, AccessTools.PropertyGetter(typeof(UnityServicesGateway), nameof(UnityServicesGateway.PlayerId)), nameof(Patches.PlayerId));
			Prefix(harmony, typeof(UnityServicesGateway), nameof(UnityServicesGateway.LoadPlayerDataAsync), nameof(Patches.LoadPlayerData));
			Prefix(harmony, typeof(UnityServicesGateway), nameof(UnityServicesGateway.SavePlayerDataAsync), nameof(Patches.SavePlayerData));
			Prefix(harmony, typeof(UnityServicesGateway), nameof(UnityServicesGateway.PushLeaderboardScoreAsync), nameof(Patches.CompletedTask));
			Prefix(harmony, typeof(UnityServicesGateway), nameof(UnityServicesGateway.GetLeaderboardScoresAsync), nameof(Patches.LeaderboardsUnavailable));
			Prefix(harmony, typeof(UnityServicesGateway), nameof(UnityServicesGateway.GetPlayerRangeAsync), nameof(Patches.LeaderboardsUnavailable));
			// Cloud Code: UnityServicesGateway.CallCloudCodeAsync<T> calls CloudCodeService.Instance, which only
			// Unity Services initialization (skipped above) would set. Install the local stand-in there.
			AccessTools.PropertySetter(typeof(CloudCodeService), nameof(CloudCodeService.Instance)).Invoke(null, new object[] { new OfflineCloudCodeService() });

			// Mining contracts and upkeep stations: served locally from captured server definitions.
			Prefix(harmony, typeof(MiningContractService), "CallAsync", nameof(Patches.MiningContracts));
			Prefix(harmony, typeof(UpkeepStationService), "CallAsync", nameof(Patches.UpkeepStations));

			// Town upkeep bonuses (sell value, Fishing XP): shown by the client but never applied; apply them.
			GameHooks.AddSellValueModifier((value, category) => OfflineSettings.TownBonuses ? OfflineUpkeep.ApplyTownSellBonus(value, category) : value);
			GameHooks.AddXpModifier((xp, skill) => OfflineSettings.TownBonuses ? OfflineUpkeep.ApplyTownXpBonus(xp, skill) : xp);

			// Display names: the UGS account name can't be updated offline; skip the sync so the change succeeds.
			// (The "change once" hint is reworded by Text/strings.json.)
			Prefix(harmony, typeof(DisplayNameService), "SyncUgsPlayerNameAsync", nameof(Patches.CompletedTask));

			// Steam -> UGS cosmetic migration needs UGS; skip it.
			Prefix(harmony, typeof(SteamLegacyCosmeticService), nameof(SteamLegacyCosmeticService.LinkSteamAndLogArcadiusCosmeticsAsync), nameof(Patches.CompletedTask));

			// Dedicated-server heartbeat: nothing to report to.
			Prefix(harmony, typeof(UgsServerHeartbeat), "Update", nameof(Patches.Skip));

			// Server select: start an in-process server right before connecting.
			Prefix(harmony, typeof(Menu_ServerSelect), nameof(Menu_ServerSelect.ConnectSelectedServer), nameof(Patches.BeforeConnect));

			// Connecting menu: continue to server select if it is re-shown after sign-in already finished.
			Prefix(harmony, typeof(Menu_Connecting), "OnEnable", nameof(Patches.ConnectingMenuEnabled));
		}

		private void ReadSettings()
		{
			OfflineSettings.UpkeepRequirementPercent = Mathf.Clamp(_config.Get("upkeepRequirementPercent", 10), 1, 100);
			OfflineSettings.TownBonuses = _config.Get("townBonuses", true);
		}

		private static void Prefix(Harmony harmony, Type type, string method, string patch)
		{
			Prefix(harmony, AccessTools.Method(type, method), patch);
		}

		private static void Prefix(Harmony harmony, MethodBase original, string patch)
		{
			harmony.Patch(original ?? throw new MissingMethodException(patch), prefix: new HarmonyMethod(typeof(Patches).GetMethod(patch, BindingFlags.Public | BindingFlags.Static)));
		}

		// Harmony prefixes: returning false skips the original method, __result is its return value.
		public static class Patches
		{
			public static bool CompletedTask(ref Task __result)
			{
				__result = Task.CompletedTask;
				return false;
			}

			public static bool Skip()
			{
				return false;
			}

			public static bool PlayerId(ref string __result)
			{
				__result = OfflineMode.PlayerId;
				return false;
			}

			public static bool LoadPlayerData(IEnumerable<string> keys, ref Task<Dictionary<string, string>> __result)
			{
				__result = OfflineMode.LoadPlayerData(keys);
				return false;
			}

			public static bool SavePlayerData(Dictionary<string, string> data, ref Task __result)
			{
				__result = OfflineMode.SavePlayerData(data);
				return false;
			}

			public static bool LeaderboardsUnavailable(ref Task<List<UnityLeaderboardEntry>> __result)
			{
				__result = OfflineMode.Unavailable<List<UnityLeaderboardEntry>>("Leaderboards");
				return false;
			}

			public static bool MiningContracts(string action, Dictionary<string, object> args, ref Task<MiningContractState> __result)
			{
				__result = OfflineContracts.Call(action, args);
				return false;
			}

			public static bool UpkeepStations(string action, string stationId, Dictionary<string, object> args, ref Task<UpkeepStationState> __result)
			{
				__result = OfflineUpkeep.Call(action, stationId, args);
				return false;
			}

			public static void BeforeConnect()
			{
				OfflineMode.EnsureLocalServer();
			}

			public static void ConnectingMenuEnabled(MonoBehaviour __instance)
			{
				OfflineMode.OnConnectingMenuEnabled(__instance);
			}
		}
	}
}
