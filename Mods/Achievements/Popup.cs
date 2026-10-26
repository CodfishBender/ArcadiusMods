using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcadiusAchievements
{
	// "Achievement unlocked" pop-ups at the top of the screen, one at a time, on an overlay canvas of their own.
	// Driven from ModEvents.Update: mod DLLs load outside Unity's serializer, so they can't add MonoBehaviours.
	internal static class Popup
	{
		private const float Fade = 0.3f;

		private const float Width = 440f;

		private const float Height = 96f;

		private static readonly Queue<(Sprite icon, string heading, string text)> Pending = new Queue<(Sprite, string, string)>();

		public static float Seconds = 5f;

		private static RectTransform _box;

		private static CanvasGroup _group;

		private static Image _icon;

		private static TextMeshProUGUI _heading;

		private static TextMeshProUGUI _text;

		private static float _shownAt = -1f;

		public static void Show(Sprite icon, string heading, string text)
		{
			Pending.Enqueue((icon, heading, text));
		}

		public static void Update()
		{
			if (_shownAt < 0f)
			{
				if (Pending.Count == 0 || !Create())
				{
					return;
				}
				(Sprite icon, string heading, string text) = Pending.Dequeue();
				_icon.sprite = icon;
				_icon.color = icon != null ? Color.white : new Color(0.85f, 0.65f, 0.2f);
				_heading.text = heading;
				_text.text = text;
				_box.gameObject.SetActive(true);
				_shownAt = Time.unscaledTime;
				Chime.Play(_box.parent.gameObject);
			}
			float age = Time.unscaledTime - _shownAt;
			float visible = Mathf.Clamp01(Mathf.Min(age, Seconds - age) / Fade);
			_group.alpha = visible;
			_box.anchoredPosition = new Vector2(0f, -24f + (1f - visible) * (Height + 24f));
			if (age >= Seconds)
			{
				_box.gameObject.SetActive(false);
				_shownAt = -1f;
			}
		}

		private static bool Create()
		{
			if (_box != null)
			{
				return true;
			}
			// The book's font once the tab exists, else the font of any text on screen.
			TMP_FontAsset font = AchievementsTab.Font;
			if (font == null)
			{
				TextMeshProUGUI any = Object.FindAnyObjectByType<TextMeshProUGUI>();
				font = any != null ? any.font : TMP_Settings.defaultFontAsset;
			}
			if (font == null)
			{
				// Wait for the game's UI to load a font.
				return false;
			}
			GameObject root = new GameObject("AchievementPopups", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
			Object.DontDestroyOnLoad(root);
			Canvas canvas = root.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 1000;
			CanvasScaler scaler = root.GetComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;

			GameObject box = new GameObject("Popup", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(Outline));
			_box = (RectTransform)box.transform;
			_box.SetParent(root.transform, false);
			_box.anchorMin = _box.anchorMax = new Vector2(0.5f, 1f);
			_box.pivot = new Vector2(0.5f, 1f);
			_box.sizeDelta = new Vector2(Width, Height);
			Image background = box.GetComponent<Image>();
			background.color = new Color(0.09f, 0.08f, 0.1f, 0.94f);
			background.raycastTarget = false;
			Outline outline = box.GetComponent<Outline>();
			outline.effectColor = new Color(0.85f, 0.65f, 0.2f, 0.9f);
			outline.effectDistance = new Vector2(2f, -2f);
			_group = box.GetComponent<CanvasGroup>();
			_group.interactable = false;
			_group.blocksRaycasts = false;

			GameObject icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
			RectTransform iconRect = (RectTransform)icon.transform;
			iconRect.SetParent(_box, false);
			iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
			iconRect.pivot = new Vector2(0f, 0.5f);
			iconRect.anchoredPosition = new Vector2(12f, 0f);
			iconRect.sizeDelta = new Vector2(Height - 24f, Height - 24f);
			_icon = icon.GetComponent<Image>();
			_icon.raycastTarget = false;

			_heading = Label("Heading", font, 18f, new Color(0.95f, 0.78f, 0.35f), 0.52f, 0.92f);
			_text = Label("Text", font, 24f, Color.white, 0.12f, 0.55f);
			_text.fontStyle = FontStyles.Bold;
			box.SetActive(false);
			return true;
		}

		private static TextMeshProUGUI Label(string name, TMP_FontAsset font, float size, Color color, float bottom, float top)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			RectTransform rect = (RectTransform)go.transform;
			rect.SetParent(_box, false);
			rect.anchorMin = new Vector2(0f, bottom);
			rect.anchorMax = new Vector2(1f, top);
			rect.offsetMin = new Vector2(Height, 0f);
			rect.offsetMax = new Vector2(-12f, 0f);
			TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
			label.font = font;
			label.fontSize = size;
			label.enableAutoSizing = true;
			label.fontSizeMin = 12f;
			label.fontSizeMax = size;
			label.color = color;
			label.alignment = TextAlignmentOptions.MidlineLeft;
			label.textWrappingMode = TextWrappingModes.NoWrap;
			label.overflowMode = TextOverflowModes.Ellipsis;
			label.raycastTarget = false;
			return label;
		}
	}
}
