using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

namespace ArcadiusOffline
{
	// Local stand-in for the "UpkeepStations" Cloud Code endpoint (replaces UpkeepStationService.CallAsync).
	// Station definitions (requirements scaled by the upkeepRequirementPercent setting), messages and response shapes are copied from captured server traffic
	// (2026-10-24: get for both stations, one Wood donation). Online these are community goals shared
	// by every player; offline only your own donations count.
	// Not captured, so inferred: level-up rules, how long a bonus stays active, failure messages.
	public static class OfflineUpkeep
	{
		private const string StateKey = "OFFLINE_UPKEEP_STATIONS";

		// Weekly cycles: cycle 42 ended 2026-10-29T08:00Z, so cycle 0 began 2026-01-01T08:00Z.
		private static readonly DateTime CycleEpoch = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

		// Server definitions. Offline there is only one contributor, so the requirements used are a
		// percentage of these community-wide amounts (OfflineSettings.UpkeepRequirementPercent, rounded up).
		private static readonly StationDefinition[] ServerStations =
		{
			new StationDefinition("furnace_upkeep", "Furnace Upkeep",
				Level(1, "Stoked", "+15% smelting speed", 0, 0, 15, Req("Coal", 2500), Req("Stone", 3500), Req("Gold", 200000)),
				Level(2, "Reinforced", "+30% smelting speed", 0, 0, 30, Req("Copper_Ingot", 300), Req("Coal", 5000), Req("Gold", 900000)),
				Level(3, "Industrial", "+45% smelting speed", 0, 0, 45, Req("Iron_Ingot", 350), Req("Coal", 9000), Req("Gold", 2000000))),
			new StationDefinition("town_upkeep", "Town Upkeep",
				Level(1, "Maintained", "+15% sell bonus\n+15% Fishing XP", 15, 15, 0, Req("Stone", 5000), Req("Gold", 200000), Req("Wood", 2000)),
				Level(2, "Fortified", "+30% sell bonus\n+30% Fishing XP", 30, 30, 0, Req("Iron_Ingot", 200), Req("Gold", 1000000), Req("Wood", 3500)),
				Level(3, "Prosperous", "+45% sell bonus\n+45% Fishing XP", 45, 45, 0, Req("Gold_Ingot", 75), Req("Gold", 2500000), Req("Oak_Log", 1500)))
		};

		private static StationDefinition[] _stations;

		private static int _stationsPercent;

		private static StationDefinition[] Stations
		{
			get
			{
				int percent = OfflineSettings.UpkeepRequirementPercent;
				if (_stations == null || _stationsPercent != percent)
				{
					_stations = Array.ConvertAll(ServerStations, s => s.Scaled(percent));
					_stationsPercent = percent;
				}
				return _stations;
			}
		}

		private sealed class StationDefinition
		{
			public readonly string id;

			public readonly string title;

			public readonly UpkeepLevelState[] levels;

			public StationDefinition(string id, string title, params UpkeepLevelState[] levels)
			{
				this.id = id;
				this.title = title;
				this.levels = levels;
			}

			public StationDefinition Scaled(int percent)
			{
				return new StationDefinition(id, title, Array.ConvertAll(levels, l => Level(l.level, l.title, l.bonusDescription, l.sellBonusPercent,
					l.fishingXpBonusPercent, l.smeltingSpeedBonusPercent, l.requirements.ConvertAll(r => Req(r.resourceType, Scale(r.requiredAmount, percent))).ToArray())));
			}
		}

		[Serializable]
		private class SavedContribution
		{
			public int level;

			public string resourceType;

			public int amount;
		}

		[Serializable]
		private class SavedStation
		{
			public string stationId;

			public int cycle;

			public int achievedLevel;

			// Level reached in the previous cycle; its bonus stays active through this one.
			public int previousAchievedLevel;

			public int contributionPoints;

			public List<SavedContribution> contributions = new List<SavedContribution>();
		}

		[Serializable]
		private class SavedState
		{
			public List<SavedStation> stations = new List<SavedStation>();
		}

		public static Task<UpkeepStationState> Call(string action, string stationId, Dictionary<string, object> args)
		{
			StationDefinition definition = Array.Find(Stations, s => s.id == stationId);
			if (definition == null)
			{
				return Task.FromResult(new UpkeepStationState
				{
					success = false,
					action = action,
					stationId = stationId,
					message = "Unknown upkeep station."
				});
			}
			int cycle = CurrentCycle();
			SavedState saved = Load();
			SavedStation station = GetStation(saved, stationId, cycle);
			// Also applies existing contributions if the requirements were lowered.
			UpdateAchievedLevel(definition, station);
			UpkeepDonationResult donation = null;
			string message = string.Empty;
			bool success = true;
			if (action == "donate")
			{
				success = Donate(definition, station, GetString(args, "resourceType"), GetInt(args, "amount"), out message, out donation);
			}
			Save(saved);
			UpkeepStationState state = BuildState(definition, station, cycle);
			state.success = success;
			state.action = action;
			state.message = message;
			state.donation = donation;
			return Task.FromResult(state);
		}

		private static bool Donate(StationDefinition definition, SavedStation station, string resourceType, int amount, out string message, out UpkeepDonationResult donation)
		{
			donation = null;
			if (amount <= 0)
			{
				message = "Enter an amount to donate.";
				return false;
			}
			if (station.achievedLevel >= definition.levels.Length)
			{
				message = "This station is fully upgraded for this cycle.";
				return false;
			}
			UpkeepLevelState level = definition.levels[station.achievedLevel];
			UpkeepRequirementState requirement = level.requirements.Find(r => string.Equals(r.resourceType, resourceType, StringComparison.OrdinalIgnoreCase));
			if (requirement == null)
			{
				message = resourceType + " is not needed for the next level.";
				return false;
			}
			int remaining = requirement.requiredAmount - GetContributed(station, level.level, requirement.resourceType);
			if (remaining <= 0)
			{
				message = requirement.resourceType + " is already complete for this level.";
				return false;
			}
			// The client spends the full amount up front and only refunds on failure, so never accept part of it.
			if (amount > remaining)
			{
				message = "Only " + remaining + " more " + requirement.resourceType + " is needed.";
				return false;
			}
			AddContribution(station, level.level, requirement.resourceType, amount);
			station.contributionPoints += amount;
			UpdateAchievedLevel(definition, station);
			message = "Donation added. +" + amount + " contribution points.";
			donation = new UpkeepDonationResult
			{
				acceptedAmount = amount,
				contributionPoints = amount,
				message = "Donation added."
			};
			return true;
		}

		private static void UpdateAchievedLevel(StationDefinition definition, SavedStation station)
		{
			while (station.achievedLevel < definition.levels.Length)
			{
				UpkeepLevelState level = definition.levels[station.achievedLevel];
				if (!level.requirements.TrueForAll(r => GetContributed(station, level.level, r.resourceType) >= r.requiredAmount))
				{
					break;
				}
				station.achievedLevel++;
			}
		}

		private static UpkeepStationState BuildState(StationDefinition definition, SavedStation station, int cycle)
		{
			string cycleId = cycle.ToString(CultureInfo.InvariantCulture);
			UpkeepStationState state = new UpkeepStationState
			{
				stationId = definition.id,
				title = definition.title,
				cycleId = cycleId,
				resetAt = CycleEpoch.AddDays(7 * (cycle + 1)).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
				achievedLevel = station.achievedLevel,
				activeBonusLevel = Mathf.Max(station.achievedLevel, station.previousAchievedLevel),
				// The UI reads 0 as "All upkeep levels complete."
				nextLevel = station.achievedLevel < definition.levels.Length ? station.achievedLevel + 1 : 0,
				canDonate = station.achievedLevel < definition.levels.Length,
				contributionPoints = station.contributionPoints,
				leaderboard = new UpkeepContributionLeaderboard
				{
					stationId = definition.id,
					cycleId = cycleId,
					entries = new List<UpkeepContributionEntry>()
				}
			};
			if (station.contributionPoints > 0)
			{
				state.leaderboard.entries.Add(new UpkeepContributionEntry
				{
					rank = 1,
					playerId = OfflineMode.PlayerId,
					playerName = string.IsNullOrWhiteSpace(Playfab_PlayerData.displayName) ? "Unknown" : Playfab_PlayerData.displayName.Trim(),
					points = station.contributionPoints
				});
			}
			foreach (UpkeepLevelState level in definition.levels)
			{
				UpkeepLevelState levelState = new UpkeepLevelState
				{
					level = level.level,
					title = level.title,
					bonusDescription = level.bonusDescription,
					sellBonusPercent = level.sellBonusPercent,
					fishingXpBonusPercent = level.fishingXpBonusPercent,
					smeltingSpeedBonusPercent = level.smeltingSpeedBonusPercent,
					requirements = new List<UpkeepRequirementState>()
				};
				foreach (UpkeepRequirementState requirement in level.requirements)
				{
					int contributed = Mathf.Min(requirement.requiredAmount, GetContributed(station, level.level, requirement.resourceType));
					levelState.requirements.Add(new UpkeepRequirementState
					{
						resourceType = requirement.resourceType,
						requiredAmount = requirement.requiredAmount,
						contributedAmount = contributed,
						remainingAmount = requirement.requiredAmount - contributed,
						completed = contributed >= requirement.requiredAmount
					});
				}
				levelState.completed = level.level <= station.achievedLevel;
				state.levels.Add(levelState);
			}
			return state;
		}

		private static SavedStation GetStation(SavedState saved, string stationId, int cycle)
		{
			SavedStation station = saved.stations.Find(s => s.stationId == stationId);
			if (station == null)
			{
				station = new SavedStation { stationId = stationId, cycle = cycle };
				saved.stations.Add(station);
			}
			if (station.contributions == null)
			{
				station.contributions = new List<SavedContribution>();
			}
			if (station.cycle != cycle)
			{
				// New week: progress starts over; last week's level keeps its bonus active for this week.
				station.previousAchievedLevel = station.cycle == cycle - 1 ? station.achievedLevel : 0;
				station.cycle = cycle;
				station.achievedLevel = 0;
				station.contributionPoints = 0;
				station.contributions.Clear();
			}
			return station;
		}

		private static int GetContributed(SavedStation station, int level, string resourceType)
		{
			SavedContribution entry = station.contributions.Find(c => c.level == level && c.resourceType == resourceType);
			return entry != null ? entry.amount : 0;
		}

		private static void AddContribution(SavedStation station, int level, string resourceType, int amount)
		{
			SavedContribution entry = station.contributions.Find(c => c.level == level && c.resourceType == resourceType);
			if (entry == null)
			{
				station.contributions.Add(new SavedContribution { level = level, resourceType = resourceType, amount = amount });
			}
			else
			{
				entry.amount += amount;
			}
		}

		// Sell value hook (ore, ingot, fish, wood and crop items, after research bonuses).
		// The real client shows the town sell bonus but never applies it.
		public static int ApplyTownSellBonus(int value, ResearchBonusType category)
		{
			return ApplyPercent(value, GetActiveTownLevel()?.sellBonusPercent ?? 0);
		}

		// XP hook (after research bonuses); only Fishing XP is boosted.
		public static int ApplyTownXpBonus(int xp, ResearchBonusType skill)
		{
			return skill == ResearchBonusType.FishingXpGain ? ApplyPercent(xp, GetActiveTownLevel()?.fishingXpBonusPercent ?? 0) : xp;
		}

		private static UpkeepLevelState GetActiveTownLevel()
		{
			SavedStation station = Load().stations.Find(s => s.stationId == "town_upkeep");
			if (station == null)
			{
				return null;
			}
			// Same rule as BuildState, without rolling the saved station over to the new cycle.
			int cycle = CurrentCycle();
			int level = station.cycle == cycle ? Math.Max(station.achievedLevel, station.previousAchievedLevel)
				: station.cycle == cycle - 1 ? station.achievedLevel
				: 0;
			StationDefinition town = Array.Find(Stations, d => d.id == "town_upkeep");
			return level > 0 ? town.levels[Math.Min(level, town.levels.Length) - 1] : null;
		}

		// Same rounding as ResearchService.ApplyPositivePercentBonus.
		private static int ApplyPercent(int value, int percent)
		{
			if (value <= 0 || percent <= 0)
			{
				return value;
			}
			return Math.Max(1, Mathf.CeilToInt(value * (100 + percent) / 100f));
		}

		private static int CurrentCycle()
		{
			return (int)Math.Floor((DateTime.UtcNow - CycleEpoch).TotalDays / 7.0);
		}

		// Cached because the town bonus is read every time an item's sell value is shown.
		private static SavedState _cache;

		// Newtonsoft, not JsonUtility: this DLL is loaded by the mod loader, outside Unity's serializer.
		// It reads the saves JsonUtility wrote in earlier versions.
		private static SavedState Load()
		{
			if (_cache != null)
			{
				return _cache;
			}
			SavedState saved = null;
			string json = PlayerPrefs.GetString(StateKey, string.Empty);
			if (!string.IsNullOrEmpty(json))
			{
				try
				{
					saved = Newtonsoft.Json.JsonConvert.DeserializeObject<SavedState>(json);
				}
				catch (Exception ex)
				{
					Debug.LogWarning("[Offline] Upkeep state was unreadable; starting fresh. " + ex.Message);
				}
			}
			if (saved == null)
			{
				saved = new SavedState();
			}
			if (saved.stations == null)
			{
				saved.stations = new List<SavedStation>();
			}
			_cache = saved;
			return saved;
		}

		private static void Save(SavedState saved)
		{
			PlayerPrefs.SetString(StateKey, Newtonsoft.Json.JsonConvert.SerializeObject(saved));
			PlayerPrefs.Save();
		}

		private static string GetString(Dictionary<string, object> args, string key)
		{
			return args != null && args.TryGetValue(key, out object value) && value != null ? value.ToString().Trim() : string.Empty;
		}

		private static int GetInt(Dictionary<string, object> args, string key)
		{
			return args != null && args.TryGetValue(key, out object value) && value != null ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : 0;
		}

		private static UpkeepLevelState Level(int level, string title, string bonusDescription, int sellBonusPercent, int fishingXpBonusPercent,
			int smeltingSpeedBonusPercent, params UpkeepRequirementState[] requirements)
		{
			return new UpkeepLevelState
			{
				level = level,
				title = title,
				bonusDescription = bonusDescription,
				sellBonusPercent = sellBonusPercent,
				fishingXpBonusPercent = fishingXpBonusPercent,
				smeltingSpeedBonusPercent = smeltingSpeedBonusPercent,
				requirements = new List<UpkeepRequirementState>(requirements)
			};
		}

		private static UpkeepRequirementState Req(string resourceType, int requiredAmount)
		{
			return new UpkeepRequirementState { resourceType = resourceType, requiredAmount = requiredAmount };
		}

		private static int Scale(int amount, int percent)
		{
			return Math.Max(1, (int)(((long)amount * percent + 99) / 100));
		}
	}
}
