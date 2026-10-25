using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using ArcadiusModLoader;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Server;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using HarmonyLib;
using TrilliumStudio;
using UnityEngine;

namespace ArcadiusPrivateServer
{
	// Brings back private servers, which the game had before it moved to official dedicated servers.
	// The server list gets "Host private server" (your world, open to other players over UDP), the saved
	// servers from config.json, and "Join by address..." (type host:port). Guests also need the offline mod,
	// which answers the server's account check with a local player id.
	public sealed class PrivateServerEntry : IMod
	{
		public sealed class SavedServer
		{
			public string name = "";

			public string address = "";

			public int port;
		}

		private const string IdPrefix = "private:";

		private const string HostId = IdPrefix + "host";

		private const string JoinId = IdPrefix + "join";

		private const int DefaultPort = 7770;

		private static ModContext _context;

		private static int _hostPort;

		private static int _maxPlayers;

		private static string _bindAddress;

		private static List<SavedServer> _saved;

		private static string _lastAddress;

		private static string _lastServer;

		public void OnLoad(ModContext context)
		{
			_context = context;
			ReadSettings();
			ModEvents.ConfigReloaded += ReadSettings;

			Harmony harmony = context.Harmony;
			// Runs before the offline mod's prefix, which starts a local server for loopback entries.
			harmony.Patch(AccessTools.Method(typeof(Menu_ServerSelect), nameof(Menu_ServerSelect.ConnectSelectedServer)),
				prefix: new HarmonyMethod(typeof(PrivateServerEntry), nameof(BeforeConnect)) { priority = Priority.High });
			harmony.Patch(AccessTools.Method(typeof(Menu_ServerSelect), "Populate"),
				prefix: new HarmonyMethod(typeof(PrivateServerEntry), nameof(BeforePopulate)));
			harmony.Patch(AccessTools.Method(typeof(Menu_ServerSelect), "OnServerSelectionChanged"),
				postfix: new HarmonyMethod(typeof(PrivateServerEntry), nameof(AfterSelectionChanged)));
			// The menu opens on the server last connected to (any entry, including the offline mod's).
			harmony.Patch(AccessTools.Method(typeof(Menu_ServerSelect), "GetDefaultServerIndex"),
				postfix: new HarmonyMethod(typeof(PrivateServerEntry), nameof(AfterDefaultServerIndex)));
			// Guests mine locally: the host checks each hit against its own world, which only loads the caves
			// around the host (and hides the main island while the host is in a cave), so it rejects most hits.
			harmony.Patch(AccessTools.Method(typeof(Chibi_Gathering), "PostSwing_Mining"),
				transpiler: new HarmonyMethod(typeof(PrivateServerEntry), nameof(MineLocallyTranspiler)));
		}

		// Replaces the GameManager.isOfflineMode check that picks local mining over the server request.
		public static IEnumerable<CodeInstruction> MineLocallyTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			FieldInfo offline = AccessTools.Field(typeof(GameManager), nameof(GameManager.isOfflineMode));
			MethodInfo mineLocally = AccessTools.Method(typeof(PrivateServerEntry), nameof(MineLocally));
			int replaced = 0;
			foreach (CodeInstruction instruction in instructions)
			{
				if (instruction.LoadsField(offline))
				{
					instruction.opcode = OpCodes.Call;
					instruction.operand = mineLocally;
					replaced++;
				}
				yield return instruction;
			}
			if (replaced == 0)
			{
				_context.Log.Error("Chibi_Gathering.PostSwing_Mining changed; guests' mining is still checked by the host.");
			}
		}

		public static bool MineLocally()
		{
			return GameManager.isOfflineMode || InstanceFinder.IsClientOnlyStarted;
		}

		private static void ReadSettings()
		{
			ModConfig config = _context.Config;
			_hostPort = Mathf.Clamp(config.Get("hostPort", DefaultPort), 1, 65535);
			_maxPlayers = Mathf.Clamp(config.Get("maxPlayers", 8), 1, 4095);
			_bindAddress = (config.Get("bindAddress", "") ?? "").Trim();
			_saved = config.Get("servers", new List<SavedServer>()) ?? new List<SavedServer>();
			_lastAddress = (config.Get("lastAddress", "") ?? "").Trim();
			_lastServer = config.Get("lastServer", "") ?? "";
		}

		public static void AfterDefaultServerIndex(List<GameServiceServerInfo> ___m_Servers, ref int __result)
		{
			int last = _lastServer.Length == 0 ? -1 : ___m_Servers.FindIndex(s => s != null && s.id == _lastServer);
			if (last >= 0)
			{
				__result = last;
			}
		}

		private static void Remember(string key, string value)
		{
			if (value != _context.Config.Get(key, ""))
			{
				_context.Config.Set(key, value);
			}
		}

		// Adds the private entries to whatever list the game (or the offline mod) provided.
		public static void BeforePopulate(GameServiceConfig config)
		{
			if (config == null)
			{
				return;
			}
			if (config.servers == null)
			{
				config.servers = new List<GameServiceServerInfo>();
			}
			config.servers.RemoveAll(s => s != null && s.id != null && s.id.StartsWith(IdPrefix, StringComparison.Ordinal));
			config.servers.Add(Entry(HostId, "Host private server", "127.0.0.1", _hostPort, _maxPlayers, "Host"));
			for (int i = 0; i < _saved.Count; i++)
			{
				SavedServer saved = _saved[i];
				if (saved == null || !TryParseAddress(saved.address, saved.port > 0 ? saved.port : DefaultPort, out string host, out int port))
				{
					continue;
				}
				string name = string.IsNullOrWhiteSpace(saved.name) ? host : saved.name.Trim();
				config.servers.Add(Entry(IdPrefix + "saved:" + i, name, host, port, 0, "Private"));
			}
			TryParseAddress(_lastAddress, DefaultPort, out string lastHost, out int lastPort);
			config.servers.Add(Entry(JoinId, "Join by address...", lastHost, lastPort, 0, "Private"));
		}

		public static void AfterSelectionChanged(Menu_ServerSelect __instance)
		{
			GameServiceServerInfo server = Selected(__instance);
			AddressField.Show(__instance, server != null && server.id == JoinId, _lastAddress, text => OnAddressEdited(__instance, text));
			if (server == null || !__instance.isActiveAndEnabled)
			{
				return;
			}
			if (server.id == HostId)
			{
				__instance.ShowStatusMessage("Host on UDP port " + _hostPort + " for up to " + _maxPlayers + " players. Friends join " + DescribeAddresses() + ".");
			}
			else if (server.id == JoinId && string.IsNullOrEmpty(server.ip))
			{
				__instance.ShowStatusMessage("Type the host's address, e.g. 192.168.1.20 or 192.168.1.20:" + DefaultPort + ".");
			}
		}

		private static void OnAddressEdited(Menu_ServerSelect menu, string text)
		{
			GameServiceServerInfo server = Selected(menu);
			if (server == null || server.id != JoinId)
			{
				return;
			}
			_lastAddress = (text ?? "").Trim();
			Remember("lastAddress", _lastAddress);
			if (TryParseAddress(_lastAddress, DefaultPort, out string host, out int port))
			{
				server.ip = host;
				server.port = port;
			}
			else
			{
				server.ip = "";
				server.port = 0;
			}
			// Re-runs the game's checks, so Connect is only enabled with an address.
			Traverse.Create(menu).Method("OnServerSelectionChanged", 0).GetValue();
		}

		public static void BeforeConnect(Menu_ServerSelect __instance)
		{
			GameServiceServerInfo server = Selected(__instance);
			NetworkManager networkManager = InstanceFinder.NetworkManager;
			if (server == null || networkManager == null || server.id == null)
			{
				return;
			}
			_lastServer = server.id;
			Remember("lastServer", server.id);
			if (server.id == HostId)
			{
				StartHost(networkManager);
			}
			else if (server.id.StartsWith(IdPrefix, StringComparison.Ordinal))
			{
				// A server left running from solo play would keep the world open and use the port.
				if (networkManager.ServerManager.Started)
				{
					networkManager.ServerManager.StopConnection(true);
				}
				_context.Log.Info("Joining " + server.name + " at " + server.ip + ":" + server.port + ".");
			}
		}

		private static void StartHost(NetworkManager networkManager)
		{
			Tugboat tugboat = networkManager.TransportManager.GetTransport<Tugboat>();
			if (tugboat == null)
			{
				_context.Log.Error("The game's network transport is " + networkManager.TransportManager.Transport?.GetType().Name + ", not Tugboat; can't host.");
				return;
			}
			ServerManager server = networkManager.ServerManager;
			bool configured = tugboat.GetPort() == _hostPort && tugboat.GetServerBindAddress(IPAddressType.IPv4) == _bindAddress;
			if (server.Started && !configured)
			{
				server.StopConnection(true);
			}
			tugboat.SetPort((ushort)_hostPort);
			tugboat.SetServerBindAddress(_bindAddress, IPAddressType.IPv4);
			tugboat.SetMaximumClients(_maxPlayers);
			if (!server.Started)
			{
				// The offline mod installs the account check and would start the server the same way; doing it
				// here first makes it use these settings.
				UgsAccessAuthenticator.EnsureInstalled();
				if (!server.StartConnection())
				{
					_context.Log.Error("Could not start the private server on UDP port " + _hostPort + ". Is another program using it?");
					return;
				}
			}
			_context.Log.Info("Hosting a private server on UDP port " + _hostPort + " (bind " + (_bindAddress.Length == 0 ? "all addresses" : _bindAddress) +
				", up to " + _maxPlayers + " players). Friends join " + DescribeAddresses() + ".");
		}

		private static GameServiceServerInfo Entry(string id, string name, string ip, int port, int maxPlayers, string status)
		{
			return new GameServiceServerInfo
			{
				id = id,
				name = name,
				ip = ip ?? "",
				port = port,
				playerCount = 0,
				maxPlayers = maxPlayers,
				online = true,
				status = status
			};
		}

		private static GameServiceServerInfo Selected(Menu_ServerSelect menu)
		{
			return menu == null ? null : Traverse.Create(menu).Method("GetSelectedServer").GetValue<GameServiceServerInfo>();
		}

		// "host", "host:port" or "[ipv6]:port". A port given in the text wins over defaultPort.
		internal static bool TryParseAddress(string text, int defaultPort, out string host, out int port)
		{
			host = "";
			port = defaultPort;
			text = (text ?? "").Trim();
			if (text.Length == 0)
			{
				return false;
			}
			string portText = null;
			if (text.StartsWith("[", StringComparison.Ordinal))
			{
				int end = text.IndexOf(']');
				if (end < 0)
				{
					return false;
				}
				host = text.Substring(1, end - 1);
				if (end + 1 < text.Length && text[end + 1] == ':')
				{
					portText = text.Substring(end + 2);
				}
			}
			else if (text.Count(c => c == ':') == 1)
			{
				int colon = text.IndexOf(':');
				host = text.Substring(0, colon);
				portText = text.Substring(colon + 1);
			}
			else
			{
				host = text;
			}
			if (portText != null)
			{
				if (!int.TryParse(portText, out int parsed) || parsed < 1 || parsed > 65535)
				{
					return false;
				}
				port = parsed;
			}
			return host.Length > 0;
		}

		// The host's LAN addresses with the port, for telling friends where to connect.
		private static string DescribeAddresses()
		{
			List<string> addresses = new List<string>();
			try
			{
				foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
				{
					if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
					{
						addresses.Add(address + ":" + _hostPort);
					}
				}
			}
			catch (Exception)
			{
				// Name lookup can fail without a network; the port alone still helps.
			}
			return addresses.Count == 0 ? "your IP address, port " + _hostPort : string.Join(" or ", addresses.Distinct());
		}
	}
}
