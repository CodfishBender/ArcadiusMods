using System.Collections.Generic;
using System.Text;
using ArcadiusModLoader;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcadiusAchievements
{
	// The Achievements tab of the player book (Tab key). The book's tab buttons sit in a scroll list
	// (Account, Bag, Skills, Collections, Crafting, Ranks, Settings) and BagHandler.SetBookActive shows one
	// panel per tab. This adds a copy of the Skills button after Ranks and a copy of the Skills panel: its title
	// and page art stay, its skill rows become a scrolling list of achievements on the left page, and the text
	// on the right page becomes the unlocked count above the Show and Type filters. The button opens the book
	// on Skills, then swaps in this panel; any other tab hides it again.
	internal static class AchievementsTab
	{
		private const string PanelName = "Book_Achievements_Menu";

		private const float RowHeight = 100f;

		private const float ScrollbarWidth = 10f;

		private static readonly ModLog Log = new ModLog("achievements");

		private static readonly List<Row> Rows = new List<Row>();

		// The Type filter's "All types", also the key of the totals in Refresh.
		private const string AllTypes = "";

		private static readonly List<string> Categories = new List<string>();

		private static readonly List<FilterButton> StatusButtons = new List<FilterButton>();

		private static readonly List<FilterButton> CategoryButtons = new List<FilterButton>();

		private static GameObject _panel;

		private static TextMeshProUGUI _summary;

		private static ScrollRect _list;

		private static GameObject _empty;

		// Kept while the game runs, so the book reopens with the same filters.
		private static Status _status = Status.All;

		private static string _category = AllTypes;

		public static TMP_FontAsset Font;

		private enum Status
		{
			All,
			Obtained,
			Unobtained
		}

		public static void Patch(Harmony harmony)
		{
			harmony.Patch(AccessTools.Method(typeof(BagHandler), "Start"),
				postfix: new HarmonyMethod(typeof(AchievementsTab), nameof(AfterBagStart)));
			harmony.Patch(AccessTools.Method(typeof(BagHandler), "SetBookActive"),
				postfix: new HarmonyMethod(typeof(AchievementsTab), nameof(AfterSetBookActive)));
		}

		public static void AfterBagStart(BagHandler __instance)
		{
			try
			{
				Build(__instance);
			}
			catch (System.Exception ex)
			{
				Log.Error("Could not add the Achievements tab", ex);
			}
		}

		public static void AfterSetBookActive()
		{
			if (_panel != null)
			{
				_panel.SetActive(false);
			}
		}

		public static void Refresh()
		{
			if (_panel == null || !_panel.activeInHierarchy)
			{
				return;
			}
			// Counts per category; AllTypes counts every category.
			var unlockedIn = new Dictionary<string, int> { [AllTypes] = 0 };
			var totalIn = new Dictionary<string, int> { [AllTypes] = 0 };
			int shown = 0;
			foreach (Row row in Rows)
			{
				bool unlocked = row.Update();
				string category = row.Category;
				foreach (string key in new[] { AllTypes, category })
				{
					totalIn[key] = (totalIn.TryGetValue(key, out int total) ? total : 0) + 1;
					unlockedIn[key] = (unlockedIn.TryGetValue(key, out int count) ? count : 0) + (unlocked ? 1 : 0);
				}
				bool show = (_category == AllTypes || _category == category)
					&& (_status == Status.All || unlocked == (_status == Status.Obtained));
				if (row.Root.activeSelf != show)
				{
					row.Root.SetActive(show);
				}
				shown += show ? 1 : 0;
			}
			int all = totalIn[AllTypes];
			int obtained = unlockedIn[AllTypes];
			_summary.text = $"<size=150%><b>{obtained} / {all}</b></size>\nunlocked ({(all == 0 ? 0 : obtained * 100 / all)}%)";
			_empty.SetActive(shown == 0);

			// The Show counts are within the selected type.
			int inType = totalIn.TryGetValue(_category, out int typeTotal) ? typeTotal : 0;
			int obtainedInType = unlockedIn.TryGetValue(_category, out int typeCount) ? typeCount : 0;
			StatusButtons[0].Set($"All ({inType})", _status == Status.All);
			StatusButtons[1].Set($"Obtained ({obtainedInType})", _status == Status.Obtained);
			StatusButtons[2].Set($"Unobtained ({inType - obtainedInType})", _status == Status.Unobtained);
			for (int i = 0; i < Categories.Count; i++)
			{
				string category = Categories[i];
				string name = category == AllTypes ? "All types" : category;
				int total = totalIn.TryGetValue(category, out int t) ? t : 0;
				int done = unlockedIn.TryGetValue(category, out int d) ? d : 0;
				CategoryButtons[i].Set($"{name}  <alpha=#99>{done} / {total}", _category == category);
			}
		}

		private static void SetFilter(Status status, string category)
		{
			_status = status;
			_category = category;
			Refresh();
			if (_list != null)
			{
				_list.verticalNormalizedPosition = 1f;
			}
		}

		private static void Build(BagHandler bag)
		{
			UserInterfaceBehaviour ui = UserInterfaceBehaviour.singleton;
			GameObject skills = bag.m_Book_Skills;
			if (ui == null || skills == null || ui.m_Book_Skills == null)
			{
				Log.Warning("The book's Skills tab wasn't found, so there's no Achievements tab");
				return;
			}
			if (skills.transform.parent.Find(PanelName) != null)
			{
				return;
			}

			// The tab button, after Ranks (or at the end).
			Button skillsButton = ui.m_Book_Skills;
			GameObject buttonObject = Object.Instantiate(skillsButton.gameObject, skillsButton.transform.parent, false);
			buttonObject.name = "Achievements";
			Transform after = ui.m_Book_RankingsBtn != null ? ui.m_Book_RankingsBtn.transform : null;
			buttonObject.transform.SetSiblingIndex(after != null && after.parent == buttonObject.transform.parent
				? after.GetSiblingIndex() + 1
				: buttonObject.transform.parent.childCount - 1);
			Button button = buttonObject.GetComponent<Button>();
			button.onClick = new Button.ButtonClickedEvent();
			button.onClick.AddListener(Open);
			TMP_Text label = buttonObject.GetComponentInChildren<TMP_Text>(true);
			if (label != null)
			{
				label.text = "Achievements";
				label.fontSizeMax = label.fontSize;
				label.fontSizeMin = Mathf.Min(12f, label.fontSize);
				label.enableAutoSizing = true;
				Font = label.font;
			}

			// The panel: a copy of the Skills panel, made while that is inactive so nothing in the copy wakes up
			// before its game parts (inventory, side menu, skill rows) are removed.
			bool skillsActive = skills.activeSelf;
			skills.SetActive(false);
			_panel = Object.Instantiate(skills, skills.transform.parent, false);
			skills.SetActive(skillsActive);
			_panel.name = PanelName;
			foreach (Transform child in _panel.transform)
			{
				if (child.name != "Container")
				{
					Object.Destroy(child.gameObject);
				}
			}
			Transform container = _panel.transform.Find("Container");
			Transform holder = container != null ? container.Find("Holder") : null;
			if (holder == null || holder.childCount == 0)
			{
				Log.Warning("The Skills panel has changed (no Container/Holder), so there's no Achievements tab");
				Object.Destroy(_panel);
				Object.Destroy(buttonObject);
				return;
			}

			// Container: Image (BG), the title, Image (BG), Holder (the skill rows), the right page's text.
			TextMeshProUGUI title = null;
			foreach (Transform child in container)
			{
				TextMeshProUGUI text = child.GetComponent<TextMeshProUGUI>();
				if (text == null)
				{
					continue;
				}
				if (title == null)
				{
					title = text;
				}
				else
				{
					_summary = text;
				}
			}
			if (title != null)
			{
				title.text = "Achievements";
				// Longer than "Skills": shrink to fit the title's box instead of running off the page.
				title.textWrappingMode = TextWrappingModes.NoWrap;
				title.fontSizeMax = title.fontSize;
				title.fontSizeMin = Mathf.Min(12f, title.fontSize);
				title.enableAutoSizing = true;
			}
			if (_summary == null)
			{
				_summary = Object.Instantiate(title != null ? title.gameObject : label.gameObject, container, false).GetComponent<TextMeshProUGUI>();
				RectTransform rect = _summary.rectTransform;
				rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
				rect.pivot = new Vector2(1f, 0.5f);
				rect.anchoredPosition = new Vector2(-31f, -32f);
				rect.sizeDelta = new Vector2(339f, 504f);
			}
			_summary.alignment = TextAlignmentOptions.Top;
			// Clear of the page border at the top, and further from the right edge (the filters move with it).
			_summary.margin = new Vector4(0f, 10f, 0f, 0f);
			_summary.rectTransform.anchoredPosition -= new Vector2(40f, 0f);
			_summary.richText = true;
			_summary.textWrappingMode = TextWrappingModes.Normal;

			Categories.Clear();
			Categories.Add(AllTypes);
			foreach (Achievement achievement in AchievementsEntry.All)
			{
				if (!Categories.Contains(Row.CategoryOf(achievement)))
				{
					Categories.Add(Row.CategoryOf(achievement));
				}
			}
			if (!Categories.Contains(_category))
			{
				_category = AllTypes;
			}
			CreateFilters(_summary);

			// The list takes the place of the skill rows on the left page; the first row is the template.
			RectTransform holderRect = (RectTransform)holder;
			Transform template = holder.GetChild(0);
			template.SetParent(_panel.transform, false);
			template.gameObject.SetActive(false);
			RectTransform content = CreateScrollList(container, holderRect.anchoredPosition.x, _summary);
			Object.Destroy(holder.gameObject);

			Rows.Clear();
			foreach (Achievement achievement in AchievementsEntry.All)
			{
				Rows.Add(new Row(achievement, Object.Instantiate(template.gameObject, content, false)));
			}
			Object.Destroy(template.gameObject);
			_panel.SetActive(false);
			Log.Info($"Achievements tab added ({Rows.Count} rows)");
		}

		// The filters on the right page, under the unlocked count: Show (All, Obtained, Unobtained) in a row,
		// then Type (All types and each category) in a column. Each group works like radio buttons.
		private static void CreateFilters(TextMeshProUGUI style)
		{
			GameObject filters = new GameObject("Filters", typeof(RectTransform), typeof(VerticalLayoutGroup));
			RectTransform rect = (RectTransform)filters.transform;
			rect.SetParent(style.rectTransform, false);
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			// Below the two lines of the unlocked count.
			rect.offsetMin = new Vector2(10f, 0f);
			rect.offsetMax = new Vector2(-10f, -80f);
			VerticalLayoutGroup layout = filters.GetComponent<VerticalLayoutGroup>();
			layout.childControlWidth = true;
			layout.childControlHeight = true;
			layout.childForceExpandWidth = true;
			layout.childForceExpandHeight = false;
			layout.spacing = 5f;

			Heading(rect, style, "Show");
			GameObject statusRow = new GameObject("Show", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
			statusRow.transform.SetParent(rect, false);
			statusRow.GetComponent<LayoutElement>().preferredHeight = 34f;
			HorizontalLayoutGroup row = statusRow.GetComponent<HorizontalLayoutGroup>();
			row.childControlWidth = true;
			row.childControlHeight = true;
			row.childForceExpandWidth = true;
			row.childForceExpandHeight = true;
			row.spacing = 5f;
			StatusButtons.Clear();
			foreach (Status status in new[] { Status.All, Status.Obtained, Status.Unobtained })
			{
				StatusButtons.Add(new FilterButton(statusRow.transform, style, 34f, () => SetFilter(status, _category)));
			}

			LayoutElement gap = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement)).GetComponent<LayoutElement>();
			gap.transform.SetParent(rect, false);
			gap.preferredHeight = 4f;
			Heading(rect, style, "Type");
			CategoryButtons.Clear();
			foreach (string category in Categories)
			{
				CategoryButtons.Add(new FilterButton(rect, style, 30f, () => SetFilter(_status, category)));
			}
		}

		private static void Heading(Transform parent, TextMeshProUGUI style, string text)
		{
			GameObject go = new GameObject(text + " heading", typeof(RectTransform), typeof(LayoutElement));
			go.transform.SetParent(parent, false);
			go.GetComponent<LayoutElement>().preferredHeight = 22f;
			TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
			label.font = style.font;
			label.fontSize = 18f;
			label.fontStyle = FontStyles.Bold;
			label.color = style.color;
			label.alignment = TextAlignmentOptions.BottomLeft;
			label.raycastTarget = false;
			label.text = text;
		}

		// A vertical scroll list centred at x on the left page, under the title; returns its content.
		private static RectTransform CreateScrollList(Transform container, float x, TextMeshProUGUI style)
		{
			GameObject scroll = new GameObject("AchievementList", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
			RectTransform scrollRect = (RectTransform)scroll.transform;
			scrollRect.SetParent(container, false);
			scrollRect.anchorMin = scrollRect.anchorMax = new Vector2(0.5f, 0.5f);
			scrollRect.pivot = new Vector2(0.5f, 0.5f);
			scrollRect.anchoredPosition = new Vector2(x + 15f, -40f);
			scrollRect.sizeDelta = new Vector2(520f, 470f);
			// Transparent, but it catches the mouse wheel and drags.
			scroll.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

			GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
			RectTransform viewportRect = (RectTransform)viewport.transform;
			viewportRect.SetParent(scrollRect, false);
			viewportRect.anchorMin = Vector2.zero;
			viewportRect.anchorMax = Vector2.one;
			viewportRect.offsetMin = Vector2.zero;
			// Room on the right for the scrollbar.
			viewportRect.offsetMax = new Vector2(-(ScrollbarWidth + 6f), 0f);

			GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
			RectTransform contentRect = (RectTransform)content.transform;
			contentRect.SetParent(viewportRect, false);
			contentRect.anchorMin = new Vector2(0f, 1f);
			contentRect.anchorMax = new Vector2(1f, 1f);
			contentRect.pivot = new Vector2(0.5f, 1f);
			contentRect.offsetMin = contentRect.offsetMax = Vector2.zero;
			VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
			layout.childControlWidth = true;
			layout.childControlHeight = true;
			layout.childForceExpandWidth = true;
			layout.childForceExpandHeight = false;
			layout.spacing = 4f;
			layout.padding = new RectOffset(0, 4, 0, 0);
			content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			ScrollRect list = scroll.GetComponent<ScrollRect>();
			list.viewport = viewportRect;
			list.content = contentRect;
			list.horizontal = false;
			list.vertical = true;
			list.movementType = ScrollRect.MovementType.Clamped;
			list.scrollSensitivity = 10f;
			list.verticalScrollbar = CreateScrollbar(scrollRect);
			list.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
			_list = list;

			// Shown instead of the list when the filters leave nothing.
			GameObject empty = new GameObject("Empty", typeof(RectTransform));
			RectTransform emptyRect = (RectTransform)empty.transform;
			emptyRect.SetParent(viewportRect, false);
			emptyRect.anchorMin = Vector2.zero;
			emptyRect.anchorMax = Vector2.one;
			emptyRect.offsetMin = emptyRect.offsetMax = Vector2.zero;
			TextMeshProUGUI emptyText = empty.AddComponent<TextMeshProUGUI>();
			emptyText.font = style.font;
			emptyText.fontSize = 20f;
			emptyText.color = style.color;
			emptyText.alignment = TextAlignmentOptions.Center;
			emptyText.raycastTarget = false;
			emptyText.text = "No achievements here.";
			empty.SetActive(false);
			_empty = empty;
			return contentRect;
		}

		// A thin vertical scrollbar along the right edge of the list, in the book's brown.
		private static Scrollbar CreateScrollbar(RectTransform list)
		{
			GameObject bar = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
			RectTransform barRect = (RectTransform)bar.transform;
			barRect.SetParent(list, false);
			barRect.anchorMin = new Vector2(1f, 0f);
			barRect.anchorMax = Vector2.one;
			barRect.pivot = new Vector2(1f, 0.5f);
			barRect.offsetMin = new Vector2(-ScrollbarWidth, 0f);
			barRect.offsetMax = Vector2.zero;
			bar.GetComponent<Image>().color = new Color(0.25f, 0.17f, 0.1f, 0.25f);

			GameObject area = new GameObject("Sliding Area", typeof(RectTransform));
			RectTransform areaRect = (RectTransform)area.transform;
			areaRect.SetParent(barRect, false);
			areaRect.anchorMin = Vector2.zero;
			areaRect.anchorMax = Vector2.one;
			areaRect.offsetMin = areaRect.offsetMax = Vector2.zero;

			GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
			RectTransform handleRect = (RectTransform)handle.transform;
			handleRect.SetParent(areaRect, false);
			handleRect.offsetMin = handleRect.offsetMax = Vector2.zero;
			Image handleImage = handle.GetComponent<Image>();
			handleImage.color = new Color(0.4f, 0.27f, 0.15f, 0.9f);

			Scrollbar scrollbar = bar.GetComponent<Scrollbar>();
			scrollbar.handleRect = handleRect;
			scrollbar.targetGraphic = handleImage;
			scrollbar.direction = Scrollbar.Direction.BottomToTop;
			ColorBlock colors = scrollbar.colors;
			colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
			colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
			scrollbar.colors = colors;
			return scrollbar;
		}

		private static void Open()
		{
			UserInterfaceBehaviour ui = UserInterfaceBehaviour.singleton;
			if (ui == null || _panel == null)
			{
				return;
			}
			// Opens the book with the Skills panel (which also hides this one), then shows this one instead.
			ui.ChangeBookMenu(1);
			ui.m_Bag.m_Book_Skills.SetActive(false);
			_panel.SetActive(true);
			Refresh();
		}

		// One achievement, in a copy of the Skills panel's skill row: Image (icon), Xpbar > Fill (exp), and
		// Text (TMP) above the bar for the name. Adds the count beside the name and the description under the bar.
		private sealed class Row
		{
			public readonly Achievement Achievement;

			public readonly GameObject Root;

			public readonly string Category;

			private readonly Image _icon;

			private readonly Image _fill;

			private readonly TextMeshProUGUI _name;

			private readonly TextMeshProUGUI _count;

			private readonly TextMeshProUGUI _description;

			private readonly CanvasGroup _group;

			public Row(Achievement achievement, GameObject row)
			{
				Achievement = achievement;
				Root = row;
				Category = CategoryOf(achievement);
				row.name = achievement.id;
				row.SetActive(true);
				LayoutElement element = row.GetComponent<LayoutElement>();
				if (element == null)
				{
					element = row.AddComponent<LayoutElement>();
				}
				element.preferredHeight = element.minHeight = RowHeight;
				_group = row.AddComponent<CanvasGroup>();
				Transform icon = row.transform.Find("Image");
				_icon = icon != null ? icon.GetComponent<Image>() : null;
				if (_icon != null)
				{
					_icon.preserveAspect = true;
				}
				Transform fill = row.transform.Find("Xpbar/Fill (exp)");
				_fill = fill != null ? fill.GetComponent<Image>() : null;
				Transform text = row.transform.Find("Text (TMP)");
				_name = text != null ? text.GetComponent<TextMeshProUGUI>() : row.GetComponentInChildren<TextMeshProUGUI>(true);
				_name.textWrappingMode = TextWrappingModes.NoWrap;
				_name.overflowMode = TextOverflowModes.Ellipsis;
				_name.alignment = TextAlignmentOptions.BottomLeft;
				_name.fontStyle |= FontStyles.Bold;
				// Leave room on the right of the name for the count.
				_name.rectTransform.offsetMax -= new Vector2(110f, 0f);

				_count = Object.Instantiate(_name.gameObject, _name.transform.parent, false).GetComponent<TextMeshProUGUI>();
				_count.name = "Count";
				_count.rectTransform.offsetMin = new Vector2(_name.rectTransform.offsetMax.x, _name.rectTransform.offsetMin.y);
				_count.rectTransform.offsetMax = _name.rectTransform.offsetMax + new Vector2(110f, 0f);
				_count.alignment = TextAlignmentOptions.BottomRight;
				_count.fontStyle &= ~FontStyles.Bold;
				_count.fontSize = _name.fontSize * 0.8f;

				_description = Object.Instantiate(_name.gameObject, _name.transform.parent, false).GetComponent<TextMeshProUGUI>();
				_description.name = "Description";
				RectTransform rect = _description.rectTransform;
				rect.offsetMax += new Vector2(110f, 0f);
				// Below the bar (centred 15 below the row's middle, 15 tall).
				rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -24f);
				rect.sizeDelta = new Vector2(rect.sizeDelta.x, 26f);
				_description.alignment = TextAlignmentOptions.TopLeft;
				_description.fontStyle &= ~FontStyles.Bold;
				_description.fontSize = _name.fontSize * 0.75f;
				_description.enableAutoSizing = false;
			}

			// Returns whether it's unlocked.
			public bool Update()
			{
				bool unlocked = AchievementsEntry.Progress.TryGetUnlocked(Achievement.id, out System.DateTime time);
				bool secret = Achievement.hidden && !unlocked;
				long current = unlocked ? Achievement.goal : System.Math.Min(Achievement.Current(), Achievement.goal);
				if (_icon != null)
				{
					_icon.sprite = Icons.Get(Achievement.id, unlocked);
					_icon.color = _icon.sprite != null ? Color.white : unlocked ? new Color(0.85f, 0.65f, 0.2f) : Color.gray;
				}
				_name.text = secret ? "Hidden achievement" : Achievement.name;
				_description.text = secret ? "Keep playing to find out." : Achievement.description;
				if (unlocked)
				{
					_count.text = time.ToLocalTime().ToString("d MMM yyyy");
				}
				else if (secret || Achievement.goal <= 1)
				{
					_count.text = "Locked";
				}
				else
				{
					_count.text = $"{current:N0} / {Achievement.goal:N0}";
				}
				if (_fill != null)
				{
					_fill.fillAmount = secret || Achievement.goal <= 0 ? 0f : (float)current / Achievement.goal;
				}
				_group.alpha = unlocked ? 1f : 0.6f;
				return unlocked;
			}

			public static string CategoryOf(Achievement achievement)
			{
				return string.IsNullOrEmpty(achievement.category) ? "Other" : achievement.category;
			}
		}

		// One option of a filter group: filled in the book's brown when selected, faint otherwise.
		private sealed class FilterButton
		{
			private static readonly Color Selected = new Color(0.4f, 0.27f, 0.15f, 0.9f);

			private static readonly Color Unselected = new Color(0.25f, 0.17f, 0.1f, 0.15f);

			private static readonly Color SelectedText = new Color(1f, 0.95f, 0.85f);

			private readonly Image _background;

			private readonly TextMeshProUGUI _label;

			private readonly Color _text;

			public FilterButton(Transform parent, TextMeshProUGUI style, float height, UnityEngine.Events.UnityAction clicked)
			{
				GameObject go = new GameObject("Filter", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
				go.transform.SetParent(parent, false);
				LayoutElement element = go.GetComponent<LayoutElement>();
				element.preferredHeight = height;
				element.flexibleWidth = 1f;
				_background = go.GetComponent<Image>();
				Button button = go.GetComponent<Button>();
				button.targetGraphic = _background;
				button.onClick.AddListener(clicked);

				GameObject text = new GameObject("Text", typeof(RectTransform));
				RectTransform rect = (RectTransform)text.transform;
				rect.SetParent(go.transform, false);
				rect.anchorMin = Vector2.zero;
				rect.anchorMax = Vector2.one;
				rect.offsetMin = new Vector2(6f, 0f);
				rect.offsetMax = new Vector2(-6f, 0f);
				_label = text.AddComponent<TextMeshProUGUI>();
				_label.font = style.font;
				_label.fontSize = 17f;
				_label.enableAutoSizing = true;
				_label.fontSizeMin = 10f;
				_label.fontSizeMax = 17f;
				_label.alignment = TextAlignmentOptions.Center;
				_label.textWrappingMode = TextWrappingModes.NoWrap;
				_label.raycastTarget = false;
				_text = style.color;
			}

			public void Set(string text, bool selected)
			{
				_label.text = text;
				_label.color = selected ? SelectedText : _text;
				_background.color = selected ? Selected : Unselected;
			}
		}
	}
}
