using System.Collections.Generic;
using System.IO;
using ArcadiusModLoader;
using HarmonyLib;
using UnityEngine;

namespace ArcadiusAchievements
{
	// The Steam achievements, unlocked from the statistics the game already saves (levels, nodes mined, trees
	// chopped, fish caught, ores and logs sold, repairs, gold, the tool collection and loadstones), so progress
	// made before installing the mod counts too. They're checked once a second while signed in; unlocks are
	// stored per account in progress.json and shown in the player book's Achievements tab.
	public sealed class AchievementsEntry : IMod
	{
		// More unlocks than this at once (typically the first check of an existing save) give one summary pop-up.
		private const int MaxSeparatePopups = 3;

		internal static readonly List<Achievement> All = new List<Achievement>();

		internal static Progress Progress;

		private static ModConfig _config;

		private static ModLog _log;

		private static bool _showPopups;

		private static float _nextCheck;

		public void OnLoad(ModContext context)
		{
			_config = context.Config;
			_log = context.Log;
			ReadSettings();
			ModEvents.ConfigReloaded += ReadSettings;

			string model = Path.Combine(context.Directory, "Model");
			foreach (Achievement achievement in Newtonsoft.Json.JsonConvert.DeserializeObject<List<Achievement>>(File.ReadAllText(Path.Combine(model, "achievements.json"))))
			{
				achievement.Current = Achievement.StatReader(achievement.stat);
				if (achievement.Current == null)
				{
					_log.Warning($"{achievement.id}: unknown stat '{achievement.stat}', skipped");
					continue;
				}
				All.Add(achievement);
			}
			Icons.Directory = Path.Combine(model, "Icons");
			if (!Directory.Exists(Icons.Directory))
			{
				_log.Warning("No Model/Icons folder: run fetch_icons.py to get the Steam icons");
			}
			Progress = new Progress(Path.Combine(context.Directory, "progress.json"), _log);
			_log.Info($"{All.Count} achievements");

			AchievementsTab.Patch(context.Harmony);
			ModEvents.Update += Update;
		}

		private static void ReadSettings()
		{
			_showPopups = _config.Get("showPopups", true);
			Popup.Seconds = Mathf.Clamp(_config.Get("popupSeconds", 5f), 1f, 60f);
		}

		private static void Update()
		{
			Popup.Update();
			if (Time.unscaledTime < _nextCheck)
			{
				return;
			}
			_nextCheck = Time.unscaledTime + 1f;
			// Until the save has loaded, every statistic still reads 0.
			if (!Playfab_PlayerData.isIntilized)
			{
				return;
			}
			List<Achievement> unlocked = null;
			foreach (Achievement achievement in All)
			{
				if (!Progress.IsUnlocked(achievement.id) && achievement.Current() >= achievement.goal)
				{
					(unlocked = unlocked ?? new List<Achievement>()).Add(achievement);
				}
			}
			if (unlocked != null)
			{
				Unlock(unlocked);
			}
			AchievementsTab.Refresh();
		}

		private static void Unlock(List<Achievement> unlocked)
		{
			Progress.Unlock(unlocked.ConvertAll(a => a.id));
			foreach (Achievement achievement in unlocked)
			{
				_log.Info($"Unlocked {achievement.id} ({achievement.name})");
			}
			if (_showPopups)
			{
				if (unlocked.Count > MaxSeparatePopups)
				{
					Popup.Show(Icons.Get(unlocked[unlocked.Count - 1].id, true), "Achievements unlocked",
						$"{unlocked.Count} achievements");
				}
				else
				{
					foreach (Achievement achievement in unlocked)
					{
						Popup.Show(Icons.Get(achievement.id, true), "Achievement unlocked", achievement.name);
					}
				}
			}
		}
	}
}
