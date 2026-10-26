using System;
using System.Collections.Generic;
using System.IO;
using ArcadiusModLoader;

namespace ArcadiusAchievements
{
	// Unlocked achievements, kept in progress.json in the mod folder: account id -> achievement id -> unlock
	// time (UTC). Unlocks stay even if the statistic goes back down (gold spent, levels reset by prestige).
	internal sealed class Progress
	{
		private readonly string _path;

		private readonly ModLog _log;

		private Dictionary<string, Dictionary<string, DateTime>> _accounts = new Dictionary<string, Dictionary<string, DateTime>>();

		public Progress(string path, ModLog log)
		{
			_path = path;
			_log = log;
			if (!File.Exists(path))
			{
				return;
			}
			try
			{
				_accounts = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, DateTime>>>(File.ReadAllText(path))
					?? _accounts;
			}
			catch (Exception ex)
			{
				// Keep the broken file for the user to look at rather than overwriting it on the next unlock.
				string backup = path + ".broken";
				File.Copy(path, backup, true);
				_log.Error("Could not read progress.json, starting empty (copied to " + Path.GetFileName(backup) + ")", ex);
			}
		}

		// The signed-in account; the Offline mod has a single local one.
		public static string Account => string.IsNullOrEmpty(Playfab_PlayerData.playfabID) ? "local" : Playfab_PlayerData.playfabID;

		public bool TryGetUnlocked(string id, out DateTime time)
		{
			time = default;
			return _accounts.TryGetValue(Account, out Dictionary<string, DateTime> unlocked) && unlocked.TryGetValue(id, out time);
		}

		public bool IsUnlocked(string id)
		{
			return TryGetUnlocked(id, out _);
		}

		public void Unlock(IEnumerable<string> ids)
		{
			if (!_accounts.TryGetValue(Account, out Dictionary<string, DateTime> unlocked))
			{
				unlocked = _accounts[Account] = new Dictionary<string, DateTime>();
			}
			foreach (string id in ids)
			{
				unlocked[id] = DateTime.UtcNow;
			}
			try
			{
				string temp = _path + ".tmp";
				File.WriteAllText(temp, Newtonsoft.Json.JsonConvert.SerializeObject(_accounts, Newtonsoft.Json.Formatting.Indented));
				File.Copy(temp, _path, true);
				File.Delete(temp);
			}
			catch (Exception ex)
			{
				_log.Error("Could not save progress.json", ex);
			}
		}
	}
}
