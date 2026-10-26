using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArcadiusOffline
{
	// Local stand-ins for the premium shop and cosmetic ownership Cloud Code endpoints.
	// Catalog and starter cosmetics are copied from captured server traffic (2026-10-24, catalog "live-1").
	// Purchases spend the locally saved Platnum balance (earned from contracts). Purchase responses
	// were not captured; their messages are our own. Platnum packages (real-money items) are refused.
	public static class OfflineShop
	{
		// SecureSave name: an edited value fails its integrity check and the state starts fresh.
		private const string OwnedKey = "OwnedCosmetics";

		private const string CatalogVersion = "live-1";

		// What the server reported as owned for a fresh account.
		private static readonly string[] StarterCosmeticIds = { "10200", "10100", "20101", "20102", "30101", "30204" };

		private static readonly PremiumShopItem[] Catalog =
		{
			Cosmetic("21003", "Shirt", "Shirt.", 225),
			Cosmetic("11000", "", "Free HairStyle.", 0)
		};

		[Serializable]
		private class SavedOwnership
		{
			public List<string> purchasedCosmeticIds = new List<string>();
		}

		public static CosmeticOwnershipState GetOwnership()
		{
			return new CosmeticOwnershipState { message = string.Empty, ownedCosmeticIds = GetOwnedIds() };
		}

		public static PremiumShopState GetShop()
		{
			return new PremiumShopState
			{
				catalogVersion = CatalogVersion,
				items = new List<PremiumShopItem>(Catalog),
				ownedCosmeticIds = GetOwnedIds(),
				// The HUD sets its Platnum display from this value, so report the saved balance.
				platnum = Playfab_PlayerData.platnum,
				message = string.Empty
			};
		}

		public static PremiumShopPurchaseResult Purchase(Dictionary<string, object> args)
		{
			string itemId = args != null && args.TryGetValue("itemId", out object value) && value != null ? value.ToString() : string.Empty;
			PremiumShopItem item = Array.Find(Catalog, i => i.id == itemId);
			if (item == null || !item.enabled)
			{
				return Failure("That item is not available.");
			}
			List<string> owned = GetOwnedIds();
			if (owned.Contains(item.cosmeticId))
			{
				return Failure("You already own this item.");
			}
			if (Playfab_PlayerData.platnum < item.price)
			{
				return Failure("Not enough Platnum.");
			}

			SavedOwnership saved = Load();
			saved.purchasedCosmeticIds.Add(item.cosmeticId);
			SecureSave.Set(OwnedKey, Newtonsoft.Json.JsonConvert.SerializeObject(saved));
			PlayerPrefs.Save();

			// Spend before saving the character, so the new balance is what gets saved.
			Playfab_PlayerData.platnum -= item.price;
			Playfab_PlayerData.player_platinuml = Playfab_PlayerData.platnum;
			Playfab_Events.SaveCharactersData();

			PremiumShopState shop = GetShop();
			return new PremiumShopPurchaseResult
			{
				success = true,
				message = "Purchased " + (string.IsNullOrWhiteSpace(item.displayName) ? item.description.TrimEnd('.') : item.displayName) + ".",
				purchasedItemId = item.id,
				cosmeticId = item.cosmeticId,
				platnum = shop.platnum,
				ownedCosmeticIds = shop.ownedCosmeticIds,
				shop = shop
			};
		}

		public static PremiumShopPurchaseResult PurchasePlatnumPackage()
		{
			return Failure("Platnum packages are not available offline.");
		}

		private static PremiumShopPurchaseResult Failure(string message)
		{
			// Keep the balance and ownership the client applies from this result unchanged.
			return new PremiumShopPurchaseResult
			{
				success = false,
				message = message,
				platnum = Playfab_PlayerData.platnum,
				ownedCosmeticIds = GetOwnedIds()
			};
		}

		private static List<string> GetOwnedIds()
		{
			List<string> owned = new List<string>(StarterCosmeticIds);
			foreach (string id in Load().purchasedCosmeticIds)
			{
				if (!owned.Contains(id))
				{
					owned.Add(id);
				}
			}
			return owned;
		}

		// Newtonsoft, not JsonUtility: this DLL is loaded by the mod loader, outside Unity's serializer.
		// It reads the saves JsonUtility wrote in earlier versions.
		private static SavedOwnership Load()
		{
			SavedOwnership saved = null;
			if (SecureSave.Has(OwnedKey))
			{
				try
				{
					saved = Newtonsoft.Json.JsonConvert.DeserializeObject<SavedOwnership>(SecureSave.Get(OwnedKey));
				}
				catch (Exception ex)
				{
					Debug.LogWarning("[Offline] Cosmetic ownership was unreadable; starting fresh. " + ex.Message);
				}
			}
			if (saved == null)
			{
				saved = new SavedOwnership();
			}
			if (saved.purchasedCosmeticIds == null)
			{
				saved.purchasedCosmeticIds = new List<string>();
			}
			return saved;
		}

		private static PremiumShopItem Cosmetic(string id, string displayName, string description, int price)
		{
			return new PremiumShopItem
			{
				id = id,
				cosmeticId = id,
				displayName = displayName,
				description = description,
				itemType = "Cosmetic",
				price = price,
				enabled = true
			};
		}
	}
}
