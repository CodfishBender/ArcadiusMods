using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace ArcadiusModLoader
{
	// Replaces audio clips with OGG/WAV/MP3 files from each mod's Audio folder, matched by clip name.
	// Clips are swapped when an AudioSource plays them (Play, PlayOneShot, PlayClipAtPoint) and on
	// scene load for sources that play on awake.
	internal static class AudioSwaps
	{
		private static readonly ModLog Log = new ModLog("Audio");

		private static readonly Harmony Harmony = new Harmony("arcadius.modloader.audio");

		private static readonly string[] Extensions = { ".ogg", ".wav", ".mp3" };

		private static Dictionary<string, string> _files = new Dictionary<string, string>();

		// Clip name -> loaded replacement.
		private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();

		private static bool _patched;

		public static int Count => _files.Count;

		public static void Rebuild(IEnumerable<string> modDirectories)
		{
			_files = AssetFiles.Collect(modDirectories, "Audio", Extensions);
			Clips.Clear();
			if (_files.Count == 0)
			{
				return;
			}
			Log.Info(_files.Count + " audio replacement(s) found.");
			if (!_patched)
			{
				_patched = true;
				Harmony.Patch(AccessTools.Method(typeof(AudioSource), nameof(AudioSource.Play), Type.EmptyTypes), prefix: Prefix(nameof(PlayPrefix)));
				Harmony.Patch(AccessTools.Method(typeof(AudioSource), nameof(AudioSource.Play), new[] { typeof(ulong) }), prefix: Prefix(nameof(PlayPrefix)));
				Harmony.Patch(AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayOneShot), new[] { typeof(AudioClip), typeof(float) }), prefix: Prefix(nameof(PlayOneShotPrefix)));
			}
		}

		// Loads the replacement files (runs as a coroutine on the loader object).
		public static IEnumerator Load()
		{
			foreach (KeyValuePair<string, string> entry in _files)
			{
				using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(entry.Value).AbsoluteUri, GetAudioType(entry.Value)))
				{
					yield return request.SendWebRequest();
					if (request.result != UnityWebRequest.Result.Success)
					{
						Log.Error("Could not load " + entry.Value + ": " + request.error);
						continue;
					}
					AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
					clip.name = entry.Key;
					Clips[entry.Key] = clip;
				}
			}
			if (Clips.Count > 0)
			{
				Log.Info("Loaded " + Clips.Count + " replacement clip(s).");
				ApplyToSources();
			}
		}

		// For sources that started playing without a managed Play call (play on awake).
		public static void ApplyToSources()
		{
			if (Clips.Count == 0)
			{
				return;
			}
			foreach (AudioSource source in Resources.FindObjectsOfTypeAll<AudioSource>())
			{
				if (source != null && TryGetReplacement(source.clip, out AudioClip replacement))
				{
					bool playing = source.isPlaying;
					source.clip = replacement;
					if (playing)
					{
						source.Play();
					}
				}
			}
		}

		private static bool TryGetReplacement(AudioClip clip, out AudioClip replacement)
		{
			replacement = null;
			return clip != null && Clips.TryGetValue(clip.name, out replacement) && replacement != clip;
		}

		private static void PlayPrefix(AudioSource __instance)
		{
			if (TryGetReplacement(__instance.clip, out AudioClip replacement))
			{
				__instance.clip = replacement;
			}
		}

		private static void PlayOneShotPrefix(ref AudioClip clip)
		{
			if (TryGetReplacement(clip, out AudioClip replacement))
			{
				clip = replacement;
			}
		}

		private static HarmonyMethod Prefix(string name)
		{
			return new HarmonyMethod(typeof(AudioSwaps).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));
		}

		private static AudioType GetAudioType(string file)
		{
			switch (Path.GetExtension(file).ToLowerInvariant())
			{
			case ".wav":
				return AudioType.WAV;
			case ".mp3":
				return AudioType.MPEG;
			default:
				return AudioType.OGGVORBIS;
			}
		}
	}
}
