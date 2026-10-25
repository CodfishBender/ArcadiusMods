using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcadiusPrivateServer
{
	// The "Join by address..." text box. The scene's server-select layout isn't known to the mod, so the box
	// sits at the top of the news text, which is hidden while the box shows.
	internal static class AddressField
	{
		private const string Name = "PrivateServerAddress";

		private const float Height = 44f;

		public static void Show(Menu_ServerSelect menu, bool visible, string text, Action<string> edited)
		{
			TMP_Text news = Traverse.Create(menu).Field("m_NewsText").GetValue<TMP_Text>();
			if (news == null)
			{
				return;
			}
			Transform existing = news.transform.Find(Name);
			if (existing == null && !visible)
			{
				return;
			}
			TMP_InputField field = existing != null ? existing.GetComponent<TMP_InputField>() : Create(news, text, edited);
			news.enabled = !visible;
			bool appearing = visible && !field.gameObject.activeSelf;
			field.gameObject.SetActive(visible);
			// Only when it appears: editing re-runs the selection handler, and focusing again selects all the text.
			if (appearing)
			{
				field.ActivateInputField();
			}
		}

		private static TMP_InputField Create(TMP_Text news, string text, Action<string> edited)
		{
			GameObject root = new GameObject(Name, typeof(RectTransform), typeof(Image));
			root.SetActive(false);
			root.layer = news.gameObject.layer;
			RectTransform rect = (RectTransform)root.transform;
			rect.SetParent(news.rectTransform, false);
			rect.anchorMin = new Vector2(0f, 1f);
			rect.anchorMax = new Vector2(1f, 1f);
			rect.pivot = new Vector2(0.5f, 1f);
			rect.anchoredPosition = Vector2.zero;
			rect.sizeDelta = new Vector2(0f, Height);
			root.GetComponent<Image>().color = new Color(0.18f, 0.18f, 0.2f, 1f);

			GameObject area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
			area.layer = root.layer;
			RectTransform areaRect = (RectTransform)area.transform;
			areaRect.SetParent(rect, false);
			Stretch(areaRect, 12f, 6f);

			TextMeshProUGUI value = Label(areaRect, "Text", news, Color.white);
			TextMeshProUGUI placeholder = Label(areaRect, "Placeholder", news, new Color(1f, 1f, 1f, 0.45f));
			placeholder.text = "Host address, e.g. 192.168.1.20:7770";
			placeholder.fontStyle = FontStyles.Italic;

			TMP_InputField field = root.AddComponent<TMP_InputField>();
			field.textViewport = areaRect;
			field.textComponent = value;
			field.placeholder = placeholder;
			field.fontAsset = news.font;
			field.pointSize = value.fontSize;
			field.lineType = TMP_InputField.LineType.SingleLine;
			field.characterLimit = 255;
			field.text = text ?? "";
			field.onValueChanged.AddListener(changed => edited(changed));
			return field;
		}

		private static TextMeshProUGUI Label(RectTransform parent, string name, TMP_Text style, Color color)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			go.layer = parent.gameObject.layer;
			RectTransform rect = (RectTransform)go.transform;
			rect.SetParent(parent, false);
			Stretch(rect, 0f, 0f);
			TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
			label.font = style.font;
			label.fontSize = Mathf.Clamp(style.fontSize, 16f, 24f);
			label.color = color;
			label.alignment = TextAlignmentOptions.MidlineLeft;
			label.textWrappingMode = TextWrappingModes.NoWrap;
			label.raycastTarget = false;
			return label;
		}

		private static void Stretch(RectTransform rect, float horizontal, float vertical)
		{
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = new Vector2(horizontal, vertical);
			rect.offsetMax = new Vector2(-horizontal, -vertical);
		}
	}
}
