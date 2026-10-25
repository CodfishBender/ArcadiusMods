using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArcadiusModLoader
{
	// Whole-character outfits from mods (e.g. Steve, Miku), shown at the top of the wardrobe (CosmeticSystemUI).
	// Clicking one puts it on or takes it off; one outfit is worn at a time. The choice is kept on this PC
	// (PlayerPrefs), never in the account's cosmetics.
	// Other players learn about it through the game's cosmetic sync (RequestEquipFull -> RPC_EquipFull, which the
	// server relays unchecked): the top id is sent as "outfit:<id>|<top id>". Games with the loader read the outfit
	// and apply the real top; games without it can't find that top and keep the one they last saw.
	public static class Outfits
	{
		private const string CosmeticPrefix = "outfit:";

		private const string Separator = "|";

		private const string WornKey = "ModKit.Outfit";

		private sealed class Outfit
		{
			public string Id;

			public string Name;

			public Func<Sprite> Icon;

			public Cosmetic Cosmetic;
		}

		private static readonly ModLog Log = new ModLog("Outfits");

		private static readonly Harmony Harmony = new Harmony("arcadius.modloader.outfits");

		private static readonly List<Outfit> Registered = new List<Outfit>();

		// Outfit id each chibi last synced with (absent: none).
		private static readonly Dictionary<Chibi_Visual, string> WornByChibi = new Dictionary<Chibi_Visual, string>();

		private static AccessTools.FieldRef<CosmeticController, Chibi_Visual> _chibiVisual;

		private static MethodInfo _requestEquipFull;

		private static string _worn;

		// The outfit this player wears, or null.
		public static string Worn => _worn;

		// Adds an outfit to the wardrobe. icon is asked for whenever the wardrobe is refreshed; returning null
		// shows no icon. Call from IMod.OnLoad.
		public static void Register(string id, string name, Func<Sprite> icon)
		{
			if (string.IsNullOrEmpty(id) || id.Contains(Separator))
			{
				throw new ArgumentException("Outfit ids can't be empty or contain '" + Separator + "'.", nameof(id));
			}
			if (Registered.Count == 0)
			{
				Install();
			}
			Registered.RemoveAll(o => o.Id == id);
			Registered.Add(new Outfit { Id = id, Name = name, Icon = icon });
		}

		// The outfit a chibi (any player) wears, or null.
		public static string WornBy(Chibi_Visual visual)
		{
			return visual != null && WornByChibi.TryGetValue(visual, out string id) ? id : null;
		}

		private static void Install()
		{
			_worn = PlayerPrefs.GetString(WornKey, "");
			if (_worn.Length == 0)
			{
				_worn = null;
			}
			_chibiVisual = AccessTools.FieldRefAccess<CosmeticController, Chibi_Visual>("chibiVisual");
			_requestEquipFull = AccessTools.Method(typeof(CosmeticController), "RequestEquipFull") ?? throw new MissingMethodException(nameof(CosmeticController), "RequestEquipFull");
			// The observers RPC's body; its generated name ends in a signature hash.
			MethodInfo received = AccessTools.GetDeclaredMethods(typeof(CosmeticController)).FirstOrDefault(m => m.Name.StartsWith("RpcLogic___RPC_EquipFull", StringComparison.Ordinal))
				?? throw new MissingMethodException(nameof(CosmeticController), "RpcLogic___RPC_EquipFull");
			Harmony.Patch(AccessTools.Method(typeof(CosmeticSystemUI), nameof(CosmeticSystemUI.RefreshUI)), prefix: Hook(nameof(BeforeRefreshWardrobe)));
			Harmony.Patch(AccessTools.Method(typeof(Playfab_PlayerData), nameof(Playfab_PlayerData.IsCosmeticOwned)), postfix: Hook(nameof(AfterIsOwned)));
			Harmony.Patch(AccessTools.Method(typeof(CosmeticController), nameof(CosmeticController.Equip)), prefix: Hook(nameof(BeforeEquip)));
			Harmony.Patch(_requestEquipFull, prefix: Hook(nameof(BeforeRequestEquipFull)));
			Harmony.Patch(received, prefix: Hook(nameof(BeforeEquipReceived)));
		}

		private static HarmonyMethod Hook(string name)
		{
			return new HarmonyMethod(typeof(Outfits).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));
		}

		private static Outfit Find(string cosmeticId)
		{
			return cosmeticId != null && cosmeticId.StartsWith(CosmeticPrefix, StringComparison.Ordinal)
				? Registered.Find(o => cosmeticId.Length == CosmeticPrefix.Length + o.Id.Length && cosmeticId.EndsWith(o.Id, StringComparison.Ordinal))
				: null;
		}

		// Puts the outfits first in the wardrobe's list (the database asset is shared by every wardrobe).
		private static void BeforeRefreshWardrobe(CosmeticSystemUI __instance)
		{
			CosmeticDatabase database = __instance.Database;
			if (database == null)
			{
				return;
			}
			for (int i = Registered.Count - 1; i >= 0; i--)
			{
				Outfit outfit = Registered[i];
				if (outfit.Cosmetic == null)
				{
					outfit.Cosmetic = ScriptableObject.CreateInstance<Cosmetic>();
					outfit.Cosmetic.name = outfit.Cosmetic.id = CosmeticPrefix + outfit.Id;
					outfit.Cosmetic.type = CosmeticType.Top;
					outfit.Cosmetic.hideFlags = HideFlags.DontUnloadUnusedAsset;
				}
				outfit.Cosmetic.displayName = outfit.Id == _worn ? outfit.Name + " (wearing)" : outfit.Name;
				try
				{
					outfit.Cosmetic.icon = outfit.Icon?.Invoke();
				}
				catch (Exception ex)
				{
					Log.Error("Icon for outfit " + outfit.Id + " failed.", ex);
				}
				database.cosmetics.Remove(outfit.Cosmetic);
				database.cosmetics.Insert(0, outfit.Cosmetic);
			}
		}

		private static void AfterIsOwned(string cosmeticId, ref bool __result)
		{
			__result |= Find(cosmeticId) != null;
		}

		// Clicking an outfit toggles it instead of replacing the saved top.
		private static bool BeforeEquip(CosmeticController __instance, Cosmetic cosmetic)
		{
			Outfit outfit = Find(cosmetic != null ? cosmetic.id : null);
			if (outfit == null)
			{
				return true;
			}
			if (__instance.IsOwner)
			{
				_worn = _worn == outfit.Id ? null : outfit.Id;
				PlayerPrefs.SetString(WornKey, _worn ?? "");
				PlayerPrefs.Save();
				Log.Info(_worn == null ? "Took off " + outfit.Name + "." : "Wearing " + outfit.Name + ".");
				_requestEquipFull.Invoke(__instance, new object[] { Playfab_PlayerData.cosmetic_hat_id, Playfab_PlayerData.cosmetic_top_id,
					Playfab_PlayerData.cosmetic_bottom_id, Playfab_PlayerData.cosmetic_hair_id, Playfab_PlayerData.chibi_HairColor, Playfab_PlayerData.chibi_Skin });
				foreach (CosmeticSystemUI wardrobe in Object.FindObjectsByType<CosmeticSystemUI>())
				{
					wardrobe.RefreshUI();
				}
			}
			return false;
		}

		// Owner side: tag the top with the worn outfit.
		private static void BeforeRequestEquipFull(CosmeticController __instance, ref string topId)
		{
			if (__instance.IsOwner)
			{
				string top = Split(topId, out _);
				topId = _worn == null ? top : CosmeticPrefix + _worn + Separator + top;
			}
		}

		// Every player's game: note the outfit, then let the game apply the real top.
		private static void BeforeEquipReceived(CosmeticController __instance, ref string __1)
		{
			__1 = Split(__1, out string outfit);
			Chibi_Visual visual = _chibiVisual(__instance);
			WornByChibi.Keys.Where(v => v == null).ToList().ForEach(v => WornByChibi.Remove(v));
			if (visual == null)
			{
				return;
			}
			if (outfit == null)
			{
				WornByChibi.Remove(visual);
			}
			else
			{
				WornByChibi[visual] = outfit;
			}
		}

		// "outfit:<id>|<top>" -> top, with the outfit id; anything else is a plain top.
		private static string Split(string topId, out string outfit)
		{
			outfit = null;
			if (topId == null || !topId.StartsWith(CosmeticPrefix, StringComparison.Ordinal))
			{
				return topId;
			}
			int separator = topId.IndexOf(Separator, CosmeticPrefix.Length, StringComparison.Ordinal);
			if (separator < 0)
			{
				return topId;
			}
			outfit = topId.Substring(CosmeticPrefix.Length, separator - CosmeticPrefix.Length);
			return topId.Substring(separator + Separator.Length);
		}
	}
}
