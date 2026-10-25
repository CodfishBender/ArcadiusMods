using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using FishNet;
using FishNet.Managing;
using UnityEngine;

namespace ArcadiusOffline
{
	// Called from patched methods in Assembly-CSharp (see ArcadiusOfflinePatcher).
	// Replaces Unity Gaming Services with local stand-ins and hosts the game world in-process.
	public static class OfflineMode
	{
		// Same key prefix UnityServicesGateway uses for its local Cloud Save cache, so
		// progress cached while playing online is picked up offline.
		private const string LocalCachePrefix = "UGS_LOCAL_DATA_";

		private const string PlayerIdKey = "OFFLINE_PLAYER_ID";

		private const string ClientVersion = "1.0.5";

		private static string _playerId;

		public static string PlayerId
		{
			get
			{
				if (string.IsNullOrEmpty(_playerId))
				{
					_playerId = PlayerPrefs.GetString(PlayerIdKey, string.Empty);
					if (string.IsNullOrEmpty(_playerId))
					{
						_playerId = "offline-" + Guid.NewGuid().ToString("N");
						PlayerPrefs.SetString(PlayerIdKey, _playerId);
						PlayerPrefs.Save();
					}
				}
				return _playerId;
			}
		}

		public static Task Completed()
		{
			return Task.CompletedTask;
		}

		public static Task<T> Unavailable<T>(string feature)
		{
			return Task.FromException<T>(new OfflineModeException(feature));
		}

		public static Task<Dictionary<string, string>> LoadPlayerData(IEnumerable<string> keys)
		{
			Dictionary<string, string> loaded = new Dictionary<string, string>();
			foreach (string key in keys)
			{
				string prefsKey = LocalCachePrefix + key;
				if (PlayerPrefs.HasKey(prefsKey))
				{
					loaded[key] = PlayerPrefs.GetString(prefsKey);
				}
			}
			Debug.Log("[Offline] Loaded " + loaded.Count + " save key(s) from local storage.");
			return Task.FromResult(loaded);
		}

		public static Task SavePlayerData(Dictionary<string, string> data)
		{
			foreach (KeyValuePair<string, string> entry in data)
			{
				PlayerPrefs.SetString(LocalCachePrefix + entry.Key, entry.Value);
			}
			PlayerPrefs.Save();
			Debug.Log("[Offline] Saved " + data.Count + " save key(s) to local storage.");
			return Task.CompletedTask;
		}

		public static Task<T> CallCloudCode<T>(string endpoint, Dictionary<string, object> args)
		{
			object result = null;
			switch (endpoint)
			{
			case "GetGameServiceConfig":
				result = BuildLocalConfig();
				break;
			case "CheckPlayerAccess":
				result = new PlayerAccessResult { allowed = true };
				break;
			case "GetDisplayNameState":
				// Keep the saved name. Reporting "not changed" clears a one-time-change flag already in
				// the save, so the name stays editable (unlimited changes offline).
				result = new DisplayNameChangeResult
				{
					success = true,
					message = string.Empty,
					displayName = string.Empty,
					displayNameChanged = false
				};
				break;
			case "SetDisplayName":
				result = SetDisplayName(args);
				break;
			case "GetCosmeticOwnership":
				result = OfflineShop.GetOwnership();
				break;
			case "GetPremiumShop":
				result = OfflineShop.GetShop();
				break;
			case "PurchasePremiumShopItem":
				result = OfflineShop.Purchase(args);
				break;
			case "PurchasePlatnumPackage":
				result = OfflineShop.PurchasePlatnumPackage();
				break;
			}
			if (result is T typed)
			{
				return Task.FromResult(typed);
			}
			return Task.FromException<T>(new OfflineModeException("Cloud Code endpoint '" + endpoint + "'"));
		}

		// Online the server allows one change and checks the name. Offline any number of changes are
		// allowed: the result never marks the name as changed (BagHandler validates length and characters).
		private static DisplayNameChangeResult SetDisplayName(Dictionary<string, object> args)
		{
			string name = args != null && args.TryGetValue("displayName", out object value) && value != null ? value.ToString().Trim() : string.Empty;
			if (string.IsNullOrEmpty(name))
			{
				return new DisplayNameChangeResult { success = false, message = "Enter a display name." };
			}
			return new DisplayNameChangeResult
			{
				success = true,
				message = "Display name updated.",
				displayName = name,
				displayNameChanged = false
			};
		}

		// Runs before the server-select menu connects: start a FishNet server in this
		// process so the client connects to itself (host mode).
		public static void EnsureLocalServer()
		{
			NetworkManager networkManager = InstanceFinder.NetworkManager;
			if (networkManager == null || networkManager.ServerManager == null)
			{
				return;
			}
			UgsAccessAuthenticator.EnsureInstalled();
			if (networkManager.ServerManager.Started)
			{
				return;
			}
			Debug.Log("[Offline] Starting local server.");
			if (!networkManager.ServerManager.StartConnection())
			{
				Debug.LogError("[Offline] Local server failed to start.");
			}
		}

		// Runs at the start of Menu_Connecting.OnEnable. Offline sign-in completes instantly, so
		// UserInterfaceBehaviour.Start can switch back to the Connecting menu after it already
		// moved on to server select; that menu never restarts sign-in and stays on "Account ready.".
		// Resume next frame (not inline, to avoid switching canvases inside a canvas switch).
		public static void OnConnectingMenuEnabled(MonoBehaviour menu)
		{
			if (Playfab_PlayerData.isIntilized)
			{
				menu.StartCoroutine(ResumeSignIn(menu));
			}
		}

		private static IEnumerator ResumeSignIn(MonoBehaviour menu)
		{
			yield return null;
			if (menu == null || !menu.isActiveAndEnabled || !Playfab_PlayerData.isIntilized)
			{
				yield break;
			}
			if (InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Started)
			{
				yield break;
			}
			// With player data initialized, StartSignIn goes straight to server select.
			MethodInfo startSignIn = menu.GetType().GetMethod("StartSignIn", BindingFlags.Instance | BindingFlags.NonPublic);
			startSignIn?.Invoke(menu, null);
		}

		private static GameServiceConfig BuildLocalConfig()
		{
			return new GameServiceConfig
			{
				clientVersion = ClientVersion,
				minimumClientVersion = ClientVersion,
				message = "Offline mode",
				servers = new List<GameServiceServerInfo>
				{
					new GameServiceServerInfo
					{
						id = "local",
						name = "Offline (Local)",
						ip = "127.0.0.1",
						port = 0,
						playerCount = 0,
						maxPlayers = 1,
						pingMs = 0,
						online = true,
						status = "Online"
					}
				},
				news = new List<GameServiceNewsItem>()
			};
		}
	}

	public sealed class OfflineModeException : Exception
	{
		// Callers such as GlobalChatService treat "Script could not be found" as a
		// missing backend and fall back gracefully.
		public OfflineModeException(string feature)
			: base("Offline mode: " + feature + " is unavailable (Script could not be found).")
		{
		}
	}
}
