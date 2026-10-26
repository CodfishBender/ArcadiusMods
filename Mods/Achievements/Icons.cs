using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ArcadiusAchievements
{
	// The Steam icons from Model/Icons (see fetch_icons.py): <id>.jpg and <id>_locked.jpg, loaded on first use.
	// Missing icons give null, and the UI shows a plain square instead.
	internal static class Icons
	{
		private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

		public static string Directory;

		public static Sprite Get(string id, bool unlocked)
		{
			string name = unlocked ? id : id + "_locked";
			if (Cache.TryGetValue(name, out Sprite sprite) && sprite != null)
			{
				return sprite;
			}
			sprite = null;
			string path = Path.Combine(Directory, name + ".jpg");
			if (File.Exists(path))
			{
				Texture2D texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
				if (texture.LoadImage(File.ReadAllBytes(path)))
				{
					texture.name = "Achievement_" + name;
					sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
				}
			}
			Cache[name] = sprite;
			return sprite;
		}
	}
}
