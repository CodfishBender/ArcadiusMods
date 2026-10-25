using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ArcadiusOffline
{
	// Chat without Vivox (a Unity Gaming Service). The game already sends each chat message through the FishNet
	// server as a speech bubble (Chibi_NetworkObject.SendMsg, relayed to every player including the sender);
	// this sends chat only that way and also writes each relayed message into the chat panel.
	internal static class OfflineChat
	{
		private const string FallbackName = "Miner";

		private static readonly AccessTools.FieldRef<ChatManager, bool> GlobalChatReady = AccessTools.FieldRefAccess<ChatManager, bool>("isGlobalChatReady");

		// ChatManager's private helpers.
		private static readonly Action<ChatManager, string, bool, string, string> Display = Private<Action<ChatManager, string, bool, string, string>>("DisplayMessageInChat");

		private static readonly Func<ChatManager, string, string> Sanitize = Private<Func<ChatManager, string, string>>("SanitizeChatMessage");

		private static readonly Func<ChatManager, string, string> SanitizeName = Private<Func<ChatManager, string, string>>("SanitizeNickName");

		private static readonly Func<ChatManager, string, bool> IsMuted = Private<Func<ChatManager, string, bool>>("isMuted");

		private static readonly Action<ChatManager, string> HandleCommand = Private<Action<ChatManager, string>>("HandleCommand");

		private static T Private<T>(string method) where T : Delegate
		{
			return AccessTools.MethodDelegate<T>(AccessTools.Method(typeof(ChatManager), method) ?? throw new MissingMethodException(nameof(ChatManager), method));
		}

		public static void Install(Harmony harmony)
		{
			Patch(harmony, AccessTools.Method(typeof(ChatManager), "ConnectGlobalChatAsync"), prefix: nameof(Skip));
			Patch(harmony, AccessTools.Method(typeof(ChatManager), nameof(ChatManager.OpenChat)), prefix: nameof(BeforeOpenChat));
			Patch(harmony, AccessTools.Method(typeof(ChatManager), nameof(ChatManager.Send_Group_Message)), prefix: nameof(SendMessage));
			// The observers RPC's body; its generated name ends in a signature hash.
			Patch(harmony, AccessTools.GetDeclaredMethods(typeof(Chibi_NetworkObject)).FirstOrDefault(m => m.Name.StartsWith("RpcLogic___CmdSendMsg", StringComparison.Ordinal)),
				postfix: nameof(AfterMessageRelayed));
		}

		private static void Patch(Harmony harmony, MethodBase original, string prefix = null, string postfix = null)
		{
			harmony.Patch(original ?? throw new MissingMethodException(prefix ?? postfix),
				prefix: prefix == null ? null : new HarmonyMethod(typeof(OfflineChat), prefix),
				postfix: postfix == null ? null : new HarmonyMethod(typeof(OfflineChat), postfix));
		}

		public static bool Skip()
		{
			return false;
		}

		// Marks chat ready so opening it doesn't print "Global chat is connecting...".
		public static void BeforeOpenChat(ChatManager __instance)
		{
			GlobalChatReady(__instance) = true;
		}

		// Replaces Send_Group_Message: commands, the game's filter, then the networked speech bubble only.
		public static bool SendMessage(ChatManager __instance, string message)
		{
			if (!ChatManager.IsMiningLevelUnlockedForChat() || string.IsNullOrWhiteSpace(message))
			{
				return false;
			}
			if (message.StartsWith("/", StringComparison.Ordinal))
			{
				HandleCommand(__instance, message);
				return false;
			}
			message = Sanitize(__instance, message);
			Chibi_NetworkObject player = Chibi_NetworkObject.LocalChibiNetworkObject;
			if (player != null && player.IsSpawned)
			{
				player.SendMsg(message);
			}
			else
			{
				Display(__instance, "<color=yellow>Not connected to a server.</color>", true, null, null);
			}
			return false;
		}

		// Runs on every player's game for each message (the sender's too), after the speech bubble shows.
		public static void AfterMessageRelayed(Chibi_NetworkObject __instance, string __0)
		{
			ChatManager chat = ChatManager.Instance;
			if (chat == null || string.IsNullOrWhiteSpace(__0))
			{
				return;
			}
			string name = SenderName(chat, __instance);
			if (IsMuted(chat, name))
			{
				return;
			}
			// No sender id: the "Ctrl + click to report" option on chat lines needs the online services.
			Display(chat, name + " : " + Sanitize(chat, __0), false, null, name);
		}

		private static string SenderName(ChatManager chat, Chibi_NetworkObject player)
		{
			PlayerNameplate nameplate = player.GetComponentInChildren<PlayerNameplate>(true) ?? player.GetComponentInParent<PlayerNameplate>();
			string name = nameplate != null ? nameplate.m_DisplayName.Value : null;
			if (string.IsNullOrWhiteSpace(name))
			{
				return FallbackName;
			}
			string sanitized = SanitizeName(chat, name);
			return string.IsNullOrWhiteSpace(sanitized) ? FallbackName : sanitized;
		}
	}
}
