using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcadiusGraphicsSettings
{
	// A small frames-per-second readout in a corner of the screen, on its own overlay canvas above the game's UI.
	internal static class FpsCounter
	{
		private const float Interval = 0.5f;

		// The settings page's font, once the menu was built.
		public static TMP_FontAsset Font;

		private static GameObject _root;

		private static RectTransform _panel;

		private static TextMeshProUGUI _text;

		private static bool _visible;

		private static int _corner;

		private static int _frames;

		private static float _elapsed;

		public static void SetVisible(bool visible, int corner)
		{
			_visible = visible;
			_corner = corner;
			if (_root != null)
			{
				_root.SetActive(visible);
				Place();
			}
		}

		public static void Update()
		{
			if (!_visible)
			{
				return;
			}
			if (_root == null && !Create())
			{
				return;
			}
			_frames++;
			_elapsed += Time.unscaledDeltaTime;
			if (_elapsed >= Interval)
			{
				_text.text = Mathf.RoundToInt(_frames / _elapsed) + " FPS";
				_frames = 0;
				_elapsed = 0f;
			}
		}

		private static bool Create()
		{
			TMP_FontAsset font = Font != null ? Font : TMP_Settings.defaultFontAsset;
			if (font == null)
			{
				TMP_Text any = Object.FindAnyObjectByType<TextMeshProUGUI>();
				font = any != null ? any.font : null;
			}
			if (font == null)
			{
				return false;
			}
			_root = new GameObject("GraphicsSettings FPS", typeof(Canvas));
			Object.DontDestroyOnLoad(_root);
			Canvas canvas = _root.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = short.MaxValue;

			GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
			_panel = (RectTransform)panel.transform;
			_panel.SetParent(_root.transform, false);
			_panel.sizeDelta = new Vector2(96f, 30f);
			Image background = panel.GetComponent<Image>();
			background.color = new Color(0f, 0f, 0f, 0.55f);
			background.raycastTarget = false;

			GameObject label = new GameObject("Text", typeof(RectTransform));
			RectTransform labelRect = (RectTransform)label.transform;
			labelRect.SetParent(_panel, false);
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
			_text = label.AddComponent<TextMeshProUGUI>();
			_text.font = font;
			_text.fontSize = 20f;
			_text.color = Color.white;
			_text.alignment = TextAlignmentOptions.Center;
			_text.textWrappingMode = TextWrappingModes.NoWrap;
			_text.raycastTarget = false;
			_text.text = "-- FPS";
			Place();
			return true;
		}

		private static void Place()
		{
			if (_panel == null)
			{
				return;
			}
			bool right = _corner == 1 || _corner == 3;
			bool bottom = _corner >= 2;
			Vector2 anchor = new Vector2(right ? 1f : 0f, bottom ? 0f : 1f);
			_panel.anchorMin = _panel.anchorMax = _panel.pivot = anchor;
			_panel.anchoredPosition = new Vector2(right ? -8f : 8f, bottom ? 8f : -8f);
		}
	}
}
