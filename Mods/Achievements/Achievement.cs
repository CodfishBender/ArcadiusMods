using System;
using InventorySystem3;
using Newtonsoft.Json;

#pragma warning disable CS0649 // set by Newtonsoft

namespace ArcadiusAchievements
{
	// One entry of Model/achievements.json: the Steam achievement (same id, name, description and hidden flag)
	// and the save statistic that unlocks it once it reaches goal.
	internal sealed class Achievement
	{
		public string id;

		public string name;

		public string description;

		public bool hidden;

		public string category;

		// miningLevel, woodcuttingLevel, fishingLevel, nodesMined, treesChopped, fishCaught, oresSold, logsSold,
		// repairs, gold, crafted:<ItemType> or loadstone:<LevelTeleport>.
		public string stat;

		public long goal;

		[JsonIgnore]
		public Func<long> Current;

		// Returns null for a stat this mod doesn't know.
		public static Func<long> StatReader(string stat)
		{
			switch (stat)
			{
				case "miningLevel":
					return () => Playfab_PlayerData.miningLevel?.CurrentLevel ?? 0;
				case "woodcuttingLevel":
					return () => Playfab_PlayerData.woodcuttingLevel?.CurrentLevel ?? 0;
				case "fishingLevel":
					return () => Playfab_PlayerData.fishingLevel?.CurrentLevel ?? 0;
				case "nodesMined":
					return () => Playfab_PlayerData.statistic_NodesMined;
				case "treesChopped":
					return () => Playfab_PlayerData.statistic_TreesChopped;
				case "fishCaught":
					return () => Playfab_PlayerData.statistic_FishCaught;
				case "oresSold":
					return () => Playfab_PlayerData.statistic_OresSold;
				case "logsSold":
					return () => Playfab_PlayerData.statistic_LogsSold;
				case "repairs":
					return () => Playfab_PlayerData.statistic_RepairTimes;
				case "gold":
					return () => Playfab_PlayerData.gold.Value;
			}
			// Crafting a tool counts it in the collection (statistics_ItemsCollected), which is saved.
			if (stat.StartsWith("crafted:") && Enum.TryParse(stat.Substring("crafted:".Length), out ItemType item))
			{
				return () =>
				{
					int[] collected = Playfab_PlayerData.statistics_ItemsCollected;
					return collected != null && (int)item < collected.Length && collected[(int)item] > 0 ? 1 : 0;
				};
			}
			if (stat.StartsWith("loadstone:") && Enum.TryParse(stat.Substring("loadstone:".Length), out LevelTeleport stone))
			{
				return () =>
				{
					bool[] unlocked = Playfab_PlayerData.teleport_unlocked;
					return unlocked != null && (int)stone < unlocked.Length && unlocked[(int)stone] ? 1 : 0;
				};
			}
			return null;
		}
	}
}
