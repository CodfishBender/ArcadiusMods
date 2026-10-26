using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ArcadiusOffline
{
	// Obfuscated, tamper-evident storage for the offline save (PlayerPrefs: the registry, under Proton the prefix's user.reg).
	// The aim is only that the save can't easily be edited by hand, not real security: the secret is public (in this
	// source and the DLL), so anyone who reads it can still forge a save. Being fixed, it lets saves move between computers.
	// Each value is AES-256-CBC encrypted and authenticated with HMAC-SHA256 over its name, IV and ciphertext, so an
	// edited value, or one copied from another name, fails to load.
	// Older plaintext saves are migrated once, on first use.
	public static class SecureSave
	{
		private const string Prefix = "OFFLINE_SAVE_";

		// Set once the plaintext saves have been migrated; from then on they are never read again.
		private const string FormatKey = "OFFLINE_SAVE_FORMAT";

		private const string Format = "OFS1";

		// Do not change: every existing save was written with it and would fail to load. Not a secret (see above).
		private const string Secret = "af301977e4fe4c91b198c148309a22765a0b3841a73a44edb1a731a44bc0c3f8";

		// Plaintext saves of earlier versions: the game's local Cloud Save cache, and this mod's own stores.
		private const string LegacyPlayerPrefix = "UGS_LOCAL_DATA_";

		private static readonly Dictionary<string, string> LegacyStores = new Dictionary<string, string>
		{
			{ "MiningContracts", "OFFLINE_MINING_CONTRACTS" },
			{ "UpkeepStations", "OFFLINE_UPKEEP_STATIONS" },
			{ "OwnedCosmetics", "OFFLINE_OWNED_COSMETICS" }
		};

		private static byte[] _encryptionKey;

		private static byte[] _macKey;

		// Player data keys the game loads (PlayfabAccountSystem.LoadPlayerDataAsync).
		private static IEnumerable<string> PlayerDataKeys()
		{
			yield return Playfab_PlayerData.Collections_Key;
			yield return Playfab_PlayerData.Skills_Key;
			yield return Playfab_PlayerData.Inventory_Key;
			yield return Playfab_PlayerData.Furnace_Key;
			yield return Playfab_PlayerData.Extra_Key;
			yield return Playfab_PlayerData.Prestige_Key;
		}

		public static bool Has(string name)
		{
			EnsureMigrated();
			return PlayerPrefs.HasKey(Prefix + name);
		}

		// Returns null when nothing is saved. Throws SaveTamperedException when the stored value was modified.
		public static string Get(string name)
		{
			EnsureMigrated();
			string stored = PlayerPrefs.GetString(Prefix + name, string.Empty);
			return string.IsNullOrEmpty(stored) ? null : Decrypt(name, stored);
		}

		// Call PlayerPrefs.Save() afterwards.
		public static void Set(string name, string value)
		{
			EnsureMigrated();
			PlayerPrefs.SetString(Prefix + name, Encrypt(name, value ?? string.Empty));
		}

		private static void EnsureMigrated()
		{
			if (PlayerPrefs.HasKey(FormatKey))
			{
				return;
			}
			int migrated = 0;
			foreach (string key in PlayerDataKeys())
			{
				migrated += MigrateLegacy(key, LegacyPlayerPrefix + key);
			}
			foreach (KeyValuePair<string, string> store in LegacyStores)
			{
				migrated += MigrateLegacy(store.Key, store.Value);
			}
			// The plaintext keys are left in place (unused), so uninstalling the mod still leaves a readable save.
			PlayerPrefs.SetString(FormatKey, Format);
			PlayerPrefs.Save();
			Debug.Log("[Offline] Encrypted " + migrated + " plaintext save value(s).");
		}

		private static int MigrateLegacy(string name, string legacyKey)
		{
			if (!PlayerPrefs.HasKey(legacyKey) || PlayerPrefs.HasKey(Prefix + name))
			{
				return 0;
			}
			PlayerPrefs.SetString(Prefix + name, Encrypt(name, PlayerPrefs.GetString(legacyKey)));
			return 1;
		}

		// Stored as Format + ":" + base64(IV | ciphertext | HMAC).
		private static string Encrypt(string name, string value)
		{
			LoadKeys();
			using (Aes aes = new AesManaged { KeySize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7, Key = _encryptionKey })
			{
				aes.GenerateIV();
				byte[] plain = Encoding.UTF8.GetBytes(value);
				byte[] cipher;
				using (ICryptoTransform encryptor = aes.CreateEncryptor())
				{
					cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
				}
				byte[] mac = Mac(name, aes.IV, cipher);
				byte[] blob = new byte[aes.IV.Length + cipher.Length + mac.Length];
				Buffer.BlockCopy(aes.IV, 0, blob, 0, aes.IV.Length);
				Buffer.BlockCopy(cipher, 0, blob, aes.IV.Length, cipher.Length);
				Buffer.BlockCopy(mac, 0, blob, aes.IV.Length + cipher.Length, mac.Length);
				return Format + ":" + Convert.ToBase64String(blob);
			}
		}

		private static string Decrypt(string name, string stored)
		{
			LoadKeys();
			const int IvLength = 16;
			const int MacLength = 32;
			byte[] blob;
			try
			{
				if (!stored.StartsWith(Format + ":", StringComparison.Ordinal))
				{
					throw new FormatException("unknown format");
				}
				blob = Convert.FromBase64String(stored.Substring(Format.Length + 1));
			}
			catch (FormatException)
			{
				throw new SaveTamperedException(name);
			}
			int cipherLength = blob.Length - IvLength - MacLength;
			if (cipherLength <= 0 || cipherLength % 16 != 0)
			{
				throw new SaveTamperedException(name);
			}
			byte[] iv = new byte[IvLength];
			byte[] cipher = new byte[cipherLength];
			byte[] mac = new byte[MacLength];
			Buffer.BlockCopy(blob, 0, iv, 0, IvLength);
			Buffer.BlockCopy(blob, IvLength, cipher, 0, cipherLength);
			Buffer.BlockCopy(blob, IvLength + cipherLength, mac, 0, MacLength);
			if (!CryptographicOperations.FixedTimeEquals(mac, Mac(name, iv, cipher)))
			{
				throw new SaveTamperedException(name);
			}
			using (Aes aes = new AesManaged { KeySize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7, Key = _encryptionKey, IV = iv })
			using (ICryptoTransform decryptor = aes.CreateDecryptor())
			{
				return Encoding.UTF8.GetString(decryptor.TransformFinalBlock(cipher, 0, cipher.Length));
			}
		}

		// Covers the value's name too, so a valid value can't be moved to another name.
		private static byte[] Mac(string name, byte[] iv, byte[] cipher)
		{
			using (HMACSHA256 hmac = new HMACSHA256(_macKey))
			{
				byte[] header = Encoding.UTF8.GetBytes(Format + "\0" + name + "\0");
				hmac.TransformBlock(header, 0, header.Length, null, 0);
				hmac.TransformBlock(iv, 0, iv.Length, null, 0);
				hmac.TransformFinalBlock(cipher, 0, cipher.Length);
				return hmac.Hash;
			}
		}

		// Separate encryption and MAC keys, both derived from the embedded secret.
		private static void LoadKeys()
		{
			if (_macKey != null)
			{
				return;
			}
			byte[] secret = Encoding.UTF8.GetBytes(Secret);
			using (HMACSHA256 hmac = new HMACSHA256(secret))
			{
				_encryptionKey = hmac.ComputeHash(Encoding.UTF8.GetBytes("encryption"));
				_macKey = hmac.ComputeHash(Encoding.UTF8.GetBytes("authentication"));
			}
		}
	}

	public sealed class SaveTamperedException : Exception
	{
		public SaveTamperedException(string name)
			: base("Offline save '" + name + "' failed its integrity check: it was modified outside the game.")
		{
		}
	}
}
