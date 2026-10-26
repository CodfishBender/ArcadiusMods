using UnityEngine;

namespace ArcadiusAchievements
{
	// The sound of an unlock pop-up: a short rising bell arpeggio (C6, G6, C7), synthesized so the mod needs no
	// audio file. The clip is named AchievementChime, so the loader's audio swaps can replace it with
	// Audio/AchievementChime.ogg (or .wav, .mp3) in any mod folder. It follows the game's volume slider (saved
	// as "Volume"), which otherwise only drives the ambience.
	internal static class Chime
	{
		private const int SampleRate = 44100;

		private static readonly float[] Notes = { 1046.5f, 1568f, 2093f };

		private const float NoteGap = 0.1f;

		private const float Ring = 1.1f;

		public static bool Enabled = true;

		public static float Volume = 0.7f;

		private static AudioClip _clip;

		private static AudioSource _source;

		// host: a GameObject that lives as long as the pop-ups.
		public static void Play(GameObject host)
		{
			if (!Enabled || Volume <= 0f)
			{
				return;
			}
			if (_source == null)
			{
				_source = host.AddComponent<AudioSource>();
				_source.playOnAwake = false;
				_source.spatialBlend = 0f;
				_source.ignoreListenerPause = true;
			}
			_source.PlayOneShot(Clip(), Volume * Mathf.Clamp01(PlayerPrefs.GetFloat("Volume", 1f)));
		}

		private static AudioClip Clip()
		{
			if (_clip != null)
			{
				return _clip;
			}
			int length = (int)((NoteGap * (Notes.Length - 1) + Ring) * SampleRate);
			float[] samples = new float[length];
			for (int n = 0; n < Notes.Length; n++)
			{
				int start = (int)(n * NoteGap * SampleRate);
				for (int i = start; i < length; i++)
				{
					float t = (i - start) / (float)SampleRate;
					// Quick attack, exponential ring-out; the 2.76x partial gives the bell colour and fades sooner.
					float attack = Mathf.Min(1f, t / 0.004f);
					float tone = Mathf.Sin(2f * Mathf.PI * Notes[n] * t) * Mathf.Exp(-t * 4.5f)
						+ 0.35f * Mathf.Sin(2f * Mathf.PI * Notes[n] * 2.76f * t) * Mathf.Exp(-t * 9f)
						+ 0.2f * Mathf.Sin(2f * Mathf.PI * Notes[n] * 2f * t) * Mathf.Exp(-t * 6f);
					samples[i] += attack * tone;
				}
			}
			float peak = 0f;
			foreach (float sample in samples)
			{
				peak = Mathf.Max(peak, Mathf.Abs(sample));
			}
			// Fade the tail to silence so it doesn't click, then normalise.
			int fade = SampleRate / 10;
			for (int i = 0; i < length; i++)
			{
				float tail = Mathf.Clamp01((length - i) / (float)fade);
				samples[i] = samples[i] / peak * 0.8f * tail;
			}
			_clip = AudioClip.Create("AchievementChime", length, 1, SampleRate, false);
			_clip.SetData(samples, 0);
			return _clip;
		}
	}
}
