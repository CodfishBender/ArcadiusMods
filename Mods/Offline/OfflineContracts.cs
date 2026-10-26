using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

namespace ArcadiusOffline
{
	// Local stand-in for the "MiningContracts" Cloud Code endpoint (replaces MiningContractService.CallAsync).
	// Definitions, messages and response shapes are copied from captured server traffic
	// (2026-10-24, total levels 1-3: get, select, progress to completion, claim).
	// Failure cases were not captured; their messages are our own.
	public static class OfflineContracts
	{
		// SecureSave name: an edited value fails its integrity check and the state starts fresh.
		private const string StateKey = "MiningContracts";

		// Board resets daily at 08:00 UTC (captured resetAt was 2026-10-25T08:00:00Z).
		private const int ResetHourUtc = 8;

		private static readonly MiningContractDefinition[] Pool =
		{
			Contract("mine_gold_10", "Gold Rush", "Mine 10 Gold Ore.", "Mining", "Gold", 10, 450, 800, 10, 0),
			Contract("mine_stone_25", "Stone Run", "Mine 25 Stone Ore.", "Mining", "Stone", 25, 150, 250, 5, 0),
			Contract("chop_birch_25", "Birch Bundle", "Chop 25 Birch Logs.", "Woodcutting", "Birch_Log", 25, 210, 350, 5, 0)
		};

		[Serializable]
		private class SavedState
		{
			public string resetAt;

			public string activeId;

			public int progress;

			public List<string> claimedIds = new List<string>();
		}

		public static Task<MiningContractState> Call(string action, Dictionary<string, object> args)
		{
			SavedState saved = Load();
			List<MiningContractDefinition> available = GetAvailable(Playfab_PlayerData.TotalLevel);
			MiningContractDefinition active = Find(saved.activeId);
			MiningContractState state;
			switch (action)
			{
			case "select":
				state = Select(saved, available, active, GetString(args, "contractId"));
				break;
			case "progress":
				state = Progress(saved, active, GetString(args, "skillType"), GetString(args, "itemType"), GetInt(args, "amount"));
				break;
			case "claim":
				state = Claim(saved, active);
				break;
			default:
				state = Success(string.Empty);
				break;
			}
			Save(saved);
			Fill(state, action, saved, available);
			return Task.FromResult(state);
		}

		private static MiningContractState Select(SavedState saved, List<MiningContractDefinition> available, MiningContractDefinition active, string contractId)
		{
			MiningContractDefinition contract = available.Find(c => c.id == contractId);
			if (contract == null)
			{
				return Failure("That contract is not available.");
			}
			if (saved.claimedIds.Contains(contract.id))
			{
				return Failure("You already completed that contract today.");
			}
			if (active != null)
			{
				return Failure(active.id == contract.id ? "You are already tracking that contract." : "Finish your active contract first.");
			}
			saved.activeId = contract.id;
			saved.progress = 0;
			return Success("Contract accepted.");
		}

		private static MiningContractState Progress(SavedState saved, MiningContractDefinition active, string skillType, string itemType, int amount)
		{
			if (active == null || amount <= 0 || saved.progress >= active.requiredAmount)
			{
				return Success(string.Empty);
			}
			if (active.skillType == skillType && active.targetItemType == itemType)
			{
				saved.progress = Mathf.Min(active.requiredAmount, saved.progress + amount);
				if (saved.progress >= active.requiredAmount)
				{
					return Success("Contract complete. Return to claim it.");
				}
			}
			return Success(string.Empty);
		}

		private static MiningContractState Claim(SavedState saved, MiningContractDefinition active)
		{
			if (active == null)
			{
				return Failure("You have no active contract.");
			}
			if (saved.progress < active.requiredAmount)
			{
				return Failure("Contract is not complete yet.");
			}
			saved.claimedIds.Add(active.id);
			saved.activeId = null;
			saved.progress = 0;
			// The server reports claimed=false on a successful claim; mirrored as-is.
			MiningContractState state = Success("Contract claimed.");
			state.reward = new MiningContractReward
			{
				xp = active.rewardXp,
				gold = active.rewardGold,
				platnum = active.rewardPlatnum,
				// The client sets its Platnum balance to this value.
				platnumBalance = Playfab_PlayerData.platnum + active.rewardPlatnum,
				skillType = active.skillType
			};
			return state;
		}

		// Common response fields, reflecting the state after the action.
		private static void Fill(MiningContractState state, string action, SavedState saved, List<MiningContractDefinition> available)
		{
			MiningContractDefinition active = Find(saved.activeId);
			state.action = action;
			state.resetAt = saved.resetAt;
			// The server always answered totalLevel 0 regardless of the player's level.
			state.totalLevel = 0;
			state.contracts = available;
			state.activeContract = active;
			state.claimedContractIds = new List<string>(saved.claimedIds);
			state.progress = active != null ? saved.progress : 0;
			state.requiredAmount = active != null ? active.requiredAmount : 0;
			state.completed = active != null && saved.progress >= active.requiredAmount;
			state.allClaimed = available.Count > 0 && available.TrueForAll(c => saved.claimedIds.Contains(c.id));
		}

		// Newtonsoft, not JsonUtility: this DLL is loaded by the mod loader, outside Unity's serializer.
		// It reads the saves JsonUtility wrote in earlier versions.
		private static SavedState Load()
		{
			SavedState saved = null;
			if (SecureSave.Has(StateKey))
			{
				try
				{
					saved = Newtonsoft.Json.JsonConvert.DeserializeObject<SavedState>(SecureSave.Get(StateKey));
				}
				catch (Exception ex)
				{
					Debug.LogWarning("[Offline] Contract state was unreadable; starting fresh. " + ex.Message);
				}
			}
			if (saved == null)
			{
				saved = new SavedState();
			}
			if (saved.claimedIds == null)
			{
				saved.claimedIds = new List<string>();
			}
			// New day: completed contracts become available again. An in-progress contract is kept.
			string resetAt = NextReset(DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
			if (saved.resetAt != resetAt)
			{
				saved.resetAt = resetAt;
				saved.claimedIds.Clear();
			}
			return saved;
		}

		private static void Save(SavedState saved)
		{
			SecureSave.Set(StateKey, Newtonsoft.Json.JsonConvert.SerializeObject(saved));
			SecureSave.Save();
		}

		private static DateTime NextReset(DateTime utcNow)
		{
			DateTime reset = utcNow.Date.AddHours(ResetHourUtc);
			return utcNow < reset ? reset : reset.AddDays(1);
		}

		private static List<MiningContractDefinition> GetAvailable(int totalLevel)
		{
			return new List<MiningContractDefinition>(Array.FindAll(Pool, c => c.totalLevel <= totalLevel));
		}

		private static MiningContractDefinition Find(string id)
		{
			return string.IsNullOrEmpty(id) ? null : Array.Find(Pool, c => c.id == id);
		}

		private static MiningContractState Success(string message)
		{
			return new MiningContractState { success = true, message = message };
		}

		private static MiningContractState Failure(string message)
		{
			return new MiningContractState { success = false, message = message };
		}

		private static string GetString(Dictionary<string, object> args, string key)
		{
			return args != null && args.TryGetValue(key, out object value) && value != null ? value.ToString() : string.Empty;
		}

		private static int GetInt(Dictionary<string, object> args, string key)
		{
			return args != null && args.TryGetValue(key, out object value) && value != null ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : 0;
		}

		private static MiningContractDefinition Contract(string id, string title, string description, string skillType, string targetItemType,
			int requiredAmount, int rewardXp, int rewardGold, int rewardPlatnum, int totalLevel)
		{
			return new MiningContractDefinition
			{
				id = id,
				title = title,
				description = description,
				skillType = skillType,
				targetItemType = targetItemType,
				requiredAmount = requiredAmount,
				rewardXp = rewardXp,
				rewardGold = rewardGold,
				rewardPlatnum = rewardPlatnum,
				totalLevel = totalLevel
			};
		}
	}
}
