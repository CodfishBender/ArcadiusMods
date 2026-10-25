using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ArcadiusModLoader
{
	// Replaces loaded textures with PNG/JPG files from each mod's Textures folder, matched by texture
	// name (Textures/Shirt.png replaces every texture named "Shirt"; subfolders are just for organising).
	// The pixels are swapped in place, so every material, sprite and UI image using the texture updates.
	// A sprite atlas is one texture: its replacement must be the whole atlas at the same size.
	internal static class TextureSwaps
	{
		private static readonly ModLog Log = new ModLog("Textures");

		private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

		// Texture name -> replacement file (a later mod in load order wins).
		private static Dictionary<string, string> _files = new Dictionary<string, string>();

		// Texture -> file already loaded into it.
		private static readonly Dictionary<Texture2D, string> Applied = new Dictionary<Texture2D, string>();

		public static int Count => _files.Count;

		public static void Rebuild(IEnumerable<string> modDirectories)
		{
			_files = AssetFiles.Collect(modDirectories, "Textures", Extensions);
			Applied.Clear();
			if (_files.Count > 0)
			{
				Log.Info(_files.Count + " texture replacement(s) found.");
			}
		}

		// Swaps every loaded texture that has a replacement and hasn't been swapped yet.
		public static void Apply()
		{
			if (_files.Count == 0)
			{
				return;
			}
			foreach (Texture2D texture in Resources.FindObjectsOfTypeAll<Texture2D>())
			{
				if (texture == null || !_files.TryGetValue(texture.name, out string file))
				{
					continue;
				}
				if (Applied.TryGetValue(texture, out string done) && done == file)
				{
					continue;
				}
				Applied[texture] = file;
				Replace(texture, file);
			}
		}

		private static void Replace(Texture2D texture, string file)
		{
			try
			{
				int width = texture.width;
				int height = texture.height;
				string name = texture.name;
				if (!texture.LoadImage(File.ReadAllBytes(file)))
				{
					Log.Error("Could not decode " + file);
					return;
				}
				texture.name = name;
				if (texture.width != width || texture.height != height)
				{
					Log.Warning("Replaced " + name + " (" + width + "x" + height + ") with a " + texture.width + "x" + texture.height + " image; "
						+ "sprites cut from this texture may be misaligned.");
				}
				else
				{
					Log.Info("Replaced texture " + name);
				}
			}
			catch (Exception ex)
			{
				Log.Error("Could not replace texture " + texture.name + " with " + file, ex);
			}
		}
	}

	internal static class AssetFiles
	{
		// File name (without extension) -> path, across every mod's <folder>, later mods overriding earlier.
		public static Dictionary<string, string> Collect(IEnumerable<string> modDirectories, string folder, string[] extensions)
		{
			Dictionary<string, string> files = new Dictionary<string, string>();
			foreach (string modDirectory in modDirectories)
			{
				string root = Path.Combine(modDirectory, folder);
				if (!Directory.Exists(root))
				{
					continue;
				}
				foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
				{
					if (Array.IndexOf(extensions, Path.GetExtension(file).ToLowerInvariant()) >= 0)
					{
						files[Path.GetFileNameWithoutExtension(file)] = file;
					}
				}
			}
			return files;
		}
	}
}
