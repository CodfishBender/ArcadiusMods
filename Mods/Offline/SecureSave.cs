using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ArcadiusOffline
{
	// Obfuscated, tamper-evident storage for the offline save: one file, save_data, in the game's root folder (next to
	// Mods). Nothing is stored in PlayerPrefs (the registry). The aim is only that the save can't easily be edited by
	// hand, not real security: the secret is public (in this source and the DLL), so anyone who reads it can still forge
	// a save. Being fixed, it lets the file move between computers.
	// The file is AES-256-CBC encrypted and authenticated with HMAC-SHA256 over its IV and ciphertext, so an edited file
	// fails to load, and is then never overwritten. Saves of earlier versions are imported from PlayerPrefs while there
	// is no save_data yet.
	public static class SecureSave
	{
		private const string FileName = "save_data";

		private const string Format = "OFS1";

		// Do not change: every existing save was written with it and would fail to load. Not a secret (see above).
		private const string Secret = "af301977e4fe4c91b198c148309a22765a0b3841a73a44edb1a731a44bc0c3f8";

		// PlayerPrefs saves of earlier versions: 2.1 encrypted each value under this prefix (with the same secret and
		// format), before that they were plaintext under the legacy keys.
		private const string RegistryPrefix = "OFFLINE_SAVE_";

		private const string LegacyPlayerPrefix = "UGS_LOCAL_DATA_";

		private static readonly Dictionary<string, string> LegacyStores = new Dictionary<string, string>
		{
			{ "MiningContracts", "OFFLINE_MINING_CONTRACTS" },
			{ "UpkeepStations", "OFFLINE_UPKEEP_STATIONS" },
			{ "OwnedCosmetics", "OFFLINE_OWNED_COSMETICS" },
			{ "PlayerId", "OFFLINE_PLAYER_ID" }
		};

		private static byte[] _encryptionKey;

		private static byte[] _macKey;

		private static Dictionary<string, string> _values;

		// Set when save_data failed its integrity check: nothing loads from it and it is never overwritten.
		private static SaveTamperedException _rejected;

		private static string FilePath => Path.Combine(Path.GetDirectoryName(Application.dataPath), FileName);

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
			Load();
			return _values.ContainsKey(name);
		}

		// Returns null when nothing is saved. Throws SaveTamperedException when save_data was modified.
		public static string Get(string name)
		{
			Load();
			if (_rejected != null)
			{
				throw _rejected;
			}
			return _values.TryGetValue(name, out string value) ? value : null;
		}

		// Call Save() afterwards to write the file.
		public static void Set(string name, string value)
		{
			Load();
			_values[name] = value ?? string.Empty;
		}

		// Written to a temporary file first, so a crash mid-write leaves the previous save (or the new one) whole.
		public static void Save()
		{
			Load();
			if (_rejected != null)
			{
				Debug.LogError("[Offline] Not saving: " + FileName + " failed its integrity check and is kept as it is.");
				return;
			}
			string path = FilePath;
			string temp = path + ".tmp";
			File.WriteAllText(temp, Encrypt(FileName, Newtonsoft.Json.JsonConvert.SerializeObject(_values)));
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.Move(temp, path);
		}

		private static void Load()
		{
			if (_values != null)
			{
				return;
			}
			_values = new Dictionary<string, string>();
			string path = FilePath;
			if (!File.Exists(path) && File.Exists(path + ".tmp"))
			{
				// Interrupted between deleting the old save and moving the new one in.
				File.Move(path + ".tmp", path);
			}
			if (File.Exists(path))
			{
				try
				{
					_values = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(Decrypt(FileName, File.ReadAllText(path)))
						?? throw new SaveTamperedException(FileName);
				}
				catch (Exception ex)
				{
					_values = new Dictionary<string, string>();
					_rejected = ex as SaveTamperedException ?? new SaveTamperedException(FileName);
					Debug.LogError("[Offline] " + _rejected.Message);
				}
				return;
			}
			ImportFromPlayerPrefs();
		}

		// Reads only: the old PlayerPrefs keys are left as they are.
		private static void ImportFromPlayerPrefs()
		{
			foreach (string key in PlayerDataKeys())
			{
				Import(key, LegacyPlayerPrefix + key);
			}
			foreach (KeyValuePair<string, string> store in LegacyStores)
			{
				Import(store.Key, store.Value);
			}
			if (_values.Count > 0)
			{
				Save();
				Debug.Log("[Offline] Moved " + _values.Count + " save value(s) from PlayerPrefs to " + FilePath + ".");
			}
		}

		private static void Import(string name, string legacyKey)
		{
			string encrypted = PlayerPrefs.GetString(RegistryPrefix + name, string.Empty);
			if (!string.IsNullOrEmpty(encrypted))
			{
				try
				{
					_values[name] = Decrypt(name, encrypted);
					return;
				}
				catch (SaveTamperedException ex)
				{
					Debug.LogWarning("[Offline] Not importing: " + ex.Message);
					return;
				}
			}
			if (PlayerPrefs.HasKey(legacyKey))
			{
				_values[name] = PlayerPrefs.GetString(legacyKey);
			}
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

		// Covers the value's name too, so a valid value can't be moved to another name (2.1 encrypted each value apart).
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
