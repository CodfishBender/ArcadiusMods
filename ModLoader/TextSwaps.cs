using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ArcadiusModLoader
{
	// Note: Assembly-CSharp has a global JsonConvert (a JsonUtility wrapper); Newtonsoft's is fully qualified.
	// Replaces UI text using each mod's Text/*.json files: { "exact original text": "replacement" }.
	// Applies whenever a TextMeshPro or legacy UI Text is set from code, and on scene load for text
	// stored in scenes and prefabs.
	internal static class TextSwaps
	{
		private static readonly ModLog Log = new ModLog("Text");

		private static readonly Harmony Harmony = new Harmony("arcadius.modloader.text");

		private static Dictionary<string, string> _strings = new Dictionary<string, string>();

		private static bool _patched;

		public static int Count => _strings.Count;

		public static void Rebuild(IEnumerable<string> modDirectories)
		{
			Dictionary<string, string> strings = new Dictionary<string, string>();
			foreach (string modDirectory in modDirectories)
			{
				string root = Path.Combine(modDirectory, "Text");
				if (!Directory.Exists(root))
				{
					continue;
				}
				foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
				{
					try
					{
						foreach (KeyValuePair<string, string> entry in Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(file)))
						{
							strings[entry.Key] = entry.Value;
						}
					}
					catch (Exception ex)
					{
						Log.Error("Could not read " + file + ": " + ex.Message);
					}
				}
			}
			_strings = strings;
			if (_strings.Count == 0)
			{
				return;
			}
			Log.Info(_strings.Count + " text replacement(s) found.");
			if (!_patched)
			{
				_patched = true;
				HarmonyMethod prefix = new HarmonyMethod(typeof(TextSwaps).GetMethod(nameof(SetTextPrefix), BindingFlags.NonPublic | BindingFlags.Static));
				Harmony.Patch(AccessTools.PropertySetter(typeof(TMP_Text), nameof(TMP_Text.text)), prefix: prefix);
				Harmony.Patch(AccessTools.PropertySetter(typeof(UnityEngine.UI.Text), nameof(UnityEngine.UI.Text.text)), prefix: prefix);
			}
		}

		public static void ApplyToLoadedText()
		{
			if (_strings.Count == 0)
			{
				return;
			}
			foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
			{
				if (text != null && text.text != null && _strings.TryGetValue(text.text, out string replacement))
				{
					text.text = replacement;
				}
			}
			foreach (UnityEngine.UI.Text text in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>())
			{
				if (text != null && text.text != null && _strings.TryGetValue(text.text, out string replacement))
				{
					text.text = replacement;
				}
			}
		}

		private static void SetTextPrefix(ref string value)
		{
			if (value != null && _strings.TryGetValue(value, out string replacement))
			{
				value = replacement;
			}
		}
	}
}
