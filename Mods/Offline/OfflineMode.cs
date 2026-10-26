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
	// Called from the Harmony patches in OfflineEntry.
	// Replaces Unity Gaming Services with local stand-ins and hosts the game world in-process.
	public static class OfflineMode
	{
		// SecureSave name.
		private const string PlayerIdKey = "PlayerId";

		private const string ClientVersion = "1.0.5";

		private const string LocalServerId = "local";

		private static string _playerId;

		public static string PlayerId
		{
			get
			{
				if (string.IsNullOrEmpty(_playerId))
				{
					try
					{
						_playerId = SecureSave.Get(PlayerIdKey);
					}
					catch (SaveTamperedException)
					{
						// The save won't load either; an ID for this session only (SecureSave won't write the file).
						_playerId = "offline-" + Guid.NewGuid().ToString("N");
					}
					if (string.IsNullOrEmpty(_playerId))
					{
						_playerId = "offline-" + Guid.NewGuid().ToString("N");
						SecureSave.Set(PlayerIdKey, _playerId);
						SecureSave.Save();
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

		// Saves are encrypted and authenticated (SecureSave). A tampered save fails the load: the game shows
		// "Save data failed." and stops there, instead of starting a fresh character over the save.
		public static Task<Dictionary<string, string>> LoadPlayerData(IEnumerable<string> keys)
		{
			Dictionary<string, string> loaded = new Dictionary<string, string>();
			try
			{
				foreach (string key in keys)
				{
					string value = SecureSave.Get(key);
					if (value != null)
					{
						loaded[key] = value;
					}
				}
			}
			catch (SaveTamperedException ex)
			{
				Debug.LogError("[Offline] " + ex.Message + " Not loading the save.");
				return Task.FromException<Dictionary<string, string>>(ex);
			}
			Debug.Log("[Offline] Loaded " + loaded.Count + " save key(s) from local storage.");
			return Task.FromResult(loaded);
		}

		public static Task SavePlayerData(Dictionary<string, string> data)
		{
			foreach (KeyValuePair<string, string> entry in data)
			{
				SecureSave.Set(entry.Key, entry.Value);
			}
			SecureSave.Save();
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
		// process so the client connects to itself (host mode). Only for this mod's own entry:
		// other entries (e.g. the private server mod's) start their own server or join someone else's.
		public static void EnsureLocalServer(GameServiceServerInfo server)
		{
			NetworkManager networkManager = InstanceFinder.NetworkManager;
			if (networkManager == null || networkManager.ServerManager == null || (server != null && server.id != LocalServerId))
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

		// Runs as the game starts to quit, while everything still exists. The unmodded game never hosts in-process;
		// left running, FishNet's sockets (and LiteNetLib's threads) are only stopped in OnDestroy while Unity tears
		// the game down, and the game sometimes crashed on exit there.
		public static void StopNetworking()
		{
			NetworkManager networkManager = InstanceFinder.NetworkManager;
			if (networkManager == null)
			{
				return;
			}
			try
			{
				if (networkManager.ClientManager != null && networkManager.ClientManager.Started)
				{
					networkManager.ClientManager.StopConnection();
				}
				if (networkManager.ServerManager != null && networkManager.ServerManager.Started)
				{
					networkManager.ServerManager.StopConnection(true);
				}
				networkManager.TransportManager?.Transport?.Shutdown();
				Debug.Log("[Offline] Stopped networking before quitting.");
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Offline] Stopping networking before quitting failed: " + ex.Message);
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
						id = LocalServerId,
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
