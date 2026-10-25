using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

namespace ArcadiusModLoader
{
	// Dump hotkey: writes what is currently loaded to Mods/_dump, so mod authors know what to replace.
	//   Textures/<name>.png  every loaded texture (the file name is what a replacement must be called)
	//   textures.txt         texture names, sizes and formats
	//   audio.txt            audio clip names
	//   text.txt             UI text currently loaded (the keys for Text/*.json)
	// Only what the game has loaded so far is dumped; visit more areas and dump again to add to it.
	internal static class AssetDumper
	{
		private static readonly ModLog Log = new ModLog("Dump");

		public static bool Running { get; private set; }

		public static IEnumerator Dump(string directory)
		{
			Running = true;
			Log.Info("Dumping loaded assets to " + directory + " ...");
			string textureDirectory = Path.Combine(directory, "Textures");
			Directory.CreateDirectory(textureDirectory);

			StringBuilder textureList = new StringBuilder();
			int written = 0;
			HashSet<string> seen = new HashSet<string>();
			foreach (Texture2D texture in Resources.FindObjectsOfTypeAll<Texture2D>().OrderBy(t => t != null ? t.name : string.Empty))
			{
				if (texture == null || string.IsNullOrEmpty(texture.name) || !seen.Add(texture.name))
				{
					continue;
				}
				textureList.Append(texture.name).Append('\t').Append(texture.width).Append('x').Append(texture.height).Append('\t').Append(texture.format).Append('\n');
				string file = Path.Combine(textureDirectory, SafeFileName(texture.name) + ".png");
				if (File.Exists(file))
				{
					continue;
				}
				try
				{
					File.WriteAllBytes(file, EncodePng(texture));
					written++;
				}
				catch (Exception ex)
				{
					Log.Warning("Could not dump texture " + texture.name + ": " + ex.Message);
				}
				// Spread the work over frames so the game doesn't freeze.
				if (written % 16 == 0)
				{
					yield return null;
				}
			}
			File.WriteAllText(Path.Combine(directory, "textures.txt"), textureList.ToString());

			IEnumerable<string> clips = Resources.FindObjectsOfTypeAll<AudioClip>()
				.Where(c => c != null && !string.IsNullOrEmpty(c.name))
				.Select(c => c.name + "\t" + c.length.ToString("0.00") + "s\t" + c.channels + "ch")
				.Distinct()
				.OrderBy(s => s);
			File.WriteAllText(Path.Combine(directory, "audio.txt"), string.Join("\n", clips) + "\n");

			IEnumerable<string> texts = Resources.FindObjectsOfTypeAll<TMP_Text>().Where(t => t != null).Select(t => t.text)
				.Concat(Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>().Where(t => t != null).Select(t => t.text))
				.Where(s => !string.IsNullOrWhiteSpace(s))
				.Distinct()
				.OrderBy(s => s)
				.Select(s => Newtonsoft.Json.JsonConvert.SerializeObject(s));
			File.WriteAllText(Path.Combine(directory, "text.txt"), "One JSON string per line, usable as a key in Text/*.json.\n" + string.Join("\n", texts) + "\n");

			Log.Info("Dump finished: " + seen.Count + " texture(s) listed, " + written + " new PNG(s) written.");
			Running = false;
		}

		// Copies through a RenderTexture, so textures that aren't CPU-readable can be saved too.
		private static byte[] EncodePng(Texture2D texture)
		{
			RenderTexture temporary = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
			RenderTexture previous = RenderTexture.active;
			Texture2D copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
			try
			{
				Graphics.Blit(texture, temporary);
				RenderTexture.active = temporary;
				copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
				copy.Apply();
				return copy.EncodeToPNG();
			}
			finally
			{
				RenderTexture.active = previous;
				RenderTexture.ReleaseTemporary(temporary);
				UnityEngine.Object.Destroy(copy);
			}
		}

		private static string SafeFileName(string name)
		{
			foreach (char c in Path.GetInvalidFileNameChars())
			{
				name = name.Replace(c, '_');
			}
			return name;
		}
	}
}
