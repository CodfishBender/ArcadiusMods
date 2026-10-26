using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcadiusGraphicsSettings
{
	// Adds the rows to the game's Settings page (Book_Settings_Menu/Container/ContentHolder, a vertical layout of
	// 524x48 rows). New rows are copies of the game's own Quality (dropdown) and Sensitivity (slider) rows, with
	// the Fullscreen checkbox for toggles, so they match its style. The rows no longer fit the page, so the list
	// is moved into a scroll view.
	internal static class SettingsMenu
	{
		private const string ScrollName = "GraphicsSettings Scroll";

		private const float SideMargin = 28f;

		private static readonly List<Action> Refreshers = new List<Action>();

		private static ScrollRect _scroll;

		public static void Build(SettingsHandler handler)
		{
			RectTransform dropdownRow = handler.m_Dropdown_Quality.transform.parent as RectTransform;
			RectTransform sliderRow = handler.mouseSens.transform.parent as RectTransform;
			RectTransform holder = dropdownRow != null ? dropdownRow.parent as RectTransform : null;
			if (holder == null || sliderRow == null || holder.GetComponentInParent<ScrollRect>(true) != null)
			{
				return;
			}
			Refreshers.Clear();
			TMP_Text gameLabel = Label(dropdownRow);
			if (gameLabel != null)
			{
				FpsCounter.Font = gameLabel.font;
			}
			_scroll = MakeScrollable(holder, handler.m_Dropdown_Quality);
			Toggle checkbox = handler.fullscreenToggle;

			List<string> fpsNames = GraphicsOptions.FpsLimits.Select(FpsName).ToList();
			Dropdown(dropdownRow, holder, "FPS limit", fpsNames,
				() => FpsIndex(fpsNames),
				i => Set(ref GraphicsOptions.FpsLimit, "fpsLimit", i < GraphicsOptions.FpsLimits.Length ? GraphicsOptions.FpsLimits[i] : GraphicsOptions.FpsLimit),
				() => !GraphicsOptions.VSync);
			Checkbox(dropdownRow, checkbox, holder, "VSync", () => GraphicsOptions.VSync, on => Set(ref GraphicsOptions.VSync, "vSync", on));
			Slider(sliderRow, holder, "Field of view", 30, 120, 1, v => v.ToString(),
				() => GraphicsOptions.FieldOfView, v => Set(ref GraphicsOptions.FieldOfView, "fieldOfView", v), () => GraphicsOptions.CameraFound);
			Slider(sliderRow, holder, "Render scale", 25, 200, 5, v => v + "%",
				() => GraphicsOptions.RenderScale, v => Set(ref GraphicsOptions.RenderScale, "renderScalePercent", v));
			Dropdown(dropdownRow, holder, "Upscaling", GraphicsOptions.UpscalingNames.ToList(),
				() => GraphicsOptions.Upscaling, i => Set(ref GraphicsOptions.Upscaling, "upscaling", i, GraphicsOptions.UpscalingNames),
				() => GraphicsOptions.RenderScale < 100);
			Dropdown(dropdownRow, holder, "Anti-aliasing", GraphicsOptions.AntiAliasingNames.ToList(),
				() => GraphicsOptions.AntiAliasing, i => Set(ref GraphicsOptions.AntiAliasing, "antiAliasing", i, GraphicsOptions.AntiAliasingNames),
				() => GraphicsOptions.CameraFound);
			Dropdown(dropdownRow, holder, "Shadows", GraphicsOptions.ShadowNames.ToList(),
				() => GraphicsOptions.Shadows, i => Set(ref GraphicsOptions.Shadows, "shadows", i, GraphicsOptions.ShadowNames));
			Slider(sliderRow, holder, "Shadow distance", 10, 300, 5, v => v + " m",
				() => GraphicsOptions.ShadowDistance, v => Set(ref GraphicsOptions.ShadowDistance, "shadowDistance", v), () => GraphicsOptions.Shadows > 0);
			Dropdown(dropdownRow, holder, "Texture quality", GraphicsOptions.TextureNames.ToList(),
				() => GraphicsOptions.Textures, i => Set(ref GraphicsOptions.Textures, "textureQuality", i, GraphicsOptions.TextureNames));
			Dropdown(dropdownRow, holder, "Anisotropic filtering", GraphicsOptions.AnisotropicNames.ToList(),
				() => GraphicsOptions.Anisotropic, i => Set(ref GraphicsOptions.Anisotropic, "anisotropicFiltering", i, GraphicsOptions.AnisotropicNames));
			Slider(sliderRow, holder, "Detail distance", 25, 400, 25, v => v + "%",
				() => GraphicsOptions.DetailDistance, v => Set(ref GraphicsOptions.DetailDistance, "detailDistancePercent", v));
			if (GraphicsOptions.HasAmbientOcclusion)
			{
				Checkbox(dropdownRow, checkbox, holder, "Ambient occlusion", () => GraphicsOptions.AmbientOcclusion, on => Set(ref GraphicsOptions.AmbientOcclusion, "ambientOcclusion", on));
			}
			Checkbox(dropdownRow, checkbox, holder, "Bloom", () => GraphicsOptions.Bloom, on => Set(ref GraphicsOptions.Bloom, "bloom", on));
			Checkbox(dropdownRow, checkbox, holder, "Vignette", () => GraphicsOptions.Vignette, on => Set(ref GraphicsOptions.Vignette, "vignette", on));
			Checkbox(dropdownRow, checkbox, holder, "Show FPS", () => GraphicsOptions.ShowFps, on => Set(ref GraphicsOptions.ShowFps, "showFps", on));
			Refresh();
		}

		// Shows the current values, e.g. after F9 reloaded config.json, and opens the list at the top.
		public static void Refresh()
		{
			foreach (Action refresh in Refreshers)
			{
				refresh();
			}
		}

		public static void ScrollToTop()
		{
			if (_scroll != null)
			{
				_scroll.verticalNormalizedPosition = 1f;
			}
		}

		private static void Set<T>(ref T field, string key, T value)
		{
			field = value;
			GraphicsOptions.Save(key, value);
			Refresh();
		}

		// Choices are stored by name, so config.json stays readable.
		private static void Set(ref int field, string key, int index, string[] names)
		{
			field = index;
			GraphicsOptions.Save(key, names[index]);
			Refresh();
		}

		private static string FpsName(int fps)
		{
			return fps > 0 ? fps.ToString() : "Unlimited";
		}

		// A limit typed into config.json that isn't in the list is shown as an extra option.
		private static int FpsIndex(List<string> names)
		{
			int index = Array.IndexOf(GraphicsOptions.FpsLimits, GraphicsOptions.FpsLimit);
			if (index >= 0)
			{
				return index;
			}
			string custom = FpsName(GraphicsOptions.FpsLimit);
			if (names.Count == GraphicsOptions.FpsLimits.Length)
			{
				names.Add(custom);
			}
			names[names.Count - 1] = custom;
			return names.Count - 1;
		}

		private static ScrollRect MakeScrollable(RectTransform holder, TMP_Dropdown dropdown)
		{
			RectTransform container = (RectTransform)holder.parent;
			GameObject root = new GameObject(ScrollName, typeof(RectTransform));
			root.layer = holder.gameObject.layer;
			RectTransform rect = (RectTransform)root.transform;
			rect.SetParent(container, false);
			rect.SetSiblingIndex(holder.GetSiblingIndex());
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			// The page is 1000x623 with its title at the top left: leave room for it and the page's bottom edge.
			float height = Mathf.Max(container.rect.height, container.sizeDelta.y);
			rect.sizeDelta = new Vector2(holder.sizeDelta.x + 2f * SideMargin, Mathf.Max(200f, height - 160f));
			rect.anchoredPosition = new Vector2(holder.anchoredPosition.x, -35f);

			GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
			viewportObject.layer = root.layer;
			RectTransform viewport = (RectTransform)viewportObject.transform;
			viewport.SetParent(rect, false);
			viewport.anchorMin = Vector2.zero;
			viewport.anchorMax = Vector2.one;
			viewport.offsetMin = new Vector2(SideMargin, 0f);
			viewport.offsetMax = new Vector2(-SideMargin, 0f);
			// Invisible, but catches the mouse wheel between rows.
			viewportObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

			holder.SetParent(viewport, false);
			holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 1f);
			holder.pivot = new Vector2(0.5f, 1f);
			holder.anchoredPosition = Vector2.zero;

			ScrollRect scroll = root.AddComponent<ScrollRect>();
			scroll.viewport = viewport;
			scroll.content = holder;
			scroll.horizontal = false;
			scroll.vertical = true;
			scroll.movementType = ScrollRect.MovementType.Clamped;
			scroll.scrollSensitivity = 30f;

			Transform template = dropdown.template != null ? dropdown.template.Find("Scrollbar") : null;
			if (template != null && template.GetComponent<Scrollbar>() != null)
			{
				RectTransform bar = (RectTransform)UnityEngine.Object.Instantiate(template.gameObject, rect, false).transform;
				bar.name = "Scrollbar";
				bar.gameObject.SetActive(true);
				bar.anchorMin = new Vector2(1f, 0f);
				bar.anchorMax = Vector2.one;
				bar.pivot = new Vector2(1f, 1f);
				bar.sizeDelta = new Vector2(Mathf.Min(SideMargin - 6f, 20f), 0f);
				bar.anchoredPosition = Vector2.zero;
				scroll.verticalScrollbar = bar.GetComponent<Scrollbar>();
				scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
			}
			return scroll;
		}

		private static RectTransform Row(RectTransform template, RectTransform holder, string label)
		{
			RectTransform row = UnityEngine.Object.Instantiate(template, holder, false);
			row.name = "GraphicsSettings " + label;
			row.gameObject.SetActive(true);
			TMP_Text text = Label(row);
			if (text != null)
			{
				text.text = label;
			}
			return row;
		}

		private static TMP_Text Label(Transform row)
		{
			Transform label = row.Find("Text (TMP)");
			return label != null ? label.GetComponent<TMP_Text>() : null;
		}

		private static void Dropdown(RectTransform template, RectTransform holder, string label, List<string> options,
			Func<int> get, Action<int> set, Func<bool> interactable = null)
		{
			RectTransform row = Row(template, holder, label);
			TMP_Dropdown dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
			// The copy keeps listeners set in the scene, which would change the game's quality level.
			dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
			dropdown.ClearOptions();
			dropdown.AddOptions(options);
			dropdown.onValueChanged.AddListener(i => set(i));
			Refreshers.Add(() =>
			{
				int index = get();
				if (!dropdown.options.Select(o => o.text).SequenceEqual(options))
				{
					dropdown.ClearOptions();
					dropdown.AddOptions(options);
				}
				dropdown.SetValueWithoutNotify(Mathf.Clamp(index, 0, options.Count - 1));
				dropdown.RefreshShownValue();
				if (interactable != null)
				{
					dropdown.interactable = interactable();
				}
			});
		}

		private static void Slider(RectTransform template, RectTransform holder, string label, int min, int max, int step,
			Func<int, string> format, Func<int> get, Action<int> set, Func<bool> interactable = null)
		{
			RectTransform row = Row(template, holder, label);
			TMP_Text text = Label(row);
			Slider slider = row.GetComponentInChildren<Slider>(true);
			slider.onValueChanged = new Slider.SliderEvent();
			// Whole steps, so the value snaps to them.
			slider.wholeNumbers = true;
			slider.minValue = min / step;
			slider.maxValue = max / step;
			void Show(int value)
			{
				if (text != null)
				{
					text.text = label + ": " + format(value);
				}
			}
			slider.onValueChanged.AddListener(v =>
			{
				int value = Mathf.RoundToInt(v) * step;
				Show(value);
				set(value);
			});
			Refreshers.Add(() =>
			{
				int value = get();
				slider.SetValueWithoutNotify(Mathf.RoundToInt((float)value / step));
				Show(value);
				if (interactable != null)
				{
					slider.interactable = interactable();
				}
			});
		}

		private static void Checkbox(RectTransform template, Toggle checkbox, RectTransform holder, string label, Func<bool> get, Action<bool> set)
		{
			RectTransform row = Row(template, holder, label);
			TMP_Dropdown dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
			RectTransform place = (RectTransform)dropdown.transform;
			// The checkbox goes where the dropdowns are centred.
			float centre = place.anchoredPosition.x + place.sizeDelta.x * (0.5f - place.pivot.x);
			UnityEngine.Object.DestroyImmediate(dropdown.gameObject);

			Toggle toggle = UnityEngine.Object.Instantiate(checkbox, row, false);
			toggle.name = "Toggle";
			toggle.group = null;
			toggle.onValueChanged = new Toggle.ToggleEvent();
			RectTransform rect = (RectTransform)toggle.transform;
			rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			// The checkbox graphic is 20x20 at the top left of the game's toggle; its "Fullscreen" label is dropped.
			rect.sizeDelta = new Vector2(20f, 20f);
			rect.anchoredPosition = new Vector2(centre, 0f);
			Transform toggleLabel = rect.Find("Label");
			if (toggleLabel != null)
			{
				toggleLabel.gameObject.SetActive(false);
			}
			toggle.onValueChanged.AddListener(on => set(on));
			Refreshers.Add(() => toggle.SetIsOnWithoutNotify(get()));
		}
	}
}
