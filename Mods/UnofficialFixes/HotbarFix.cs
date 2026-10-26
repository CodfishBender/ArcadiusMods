using HarmonyLib;
using InventorySystem3;

namespace ArcadiusUnofficialFixes
{
	// UserInterfaceBehaviour.Update changes the hotbar slot on the mouse wheel (and 1-5 and the gamepad shoulders)
	// whatever menu is open, so scrolling the Settings page also scrolls the hotbar. While the page is open, the
	// slot is put back after that Update. (The HotbarIndex setter itself is small enough to be inlined, so it
	// can't be patched reliably.)
	internal static class HotbarFix
	{
		public static void Patch(Harmony harmony)
		{
			// UserInterfaceBehaviour is a NetworkBehaviour, so it's named rather than referenced.
			harmony.Patch(AccessTools.Method("UserInterfaceBehaviour:Update"),
				prefix: new HarmonyMethod(typeof(HotbarFix), nameof(BeforeUpdate)),
				postfix: new HarmonyMethod(typeof(HotbarFix), nameof(AfterUpdate)));
		}

		public static void BeforeUpdate(out int __state)
		{
			__state = InventoryManager.HotbarIndex;
		}

		public static void AfterUpdate(int __state)
		{
			if (UnofficialFixesEntry.NoHotbarScrollInSettings && SettingsOpen() && InventoryManager.HotbarIndex != __state)
			{
				InventoryManager.HotbarIndex = __state;
			}
		}

		// The Settings page is a page of the bag's book; its Quality dropdown is visible only while it's open.
		private static bool SettingsOpen()
		{
			SettingsHandler settings = SettingsHandler.singleton;
			return settings != null && settings.m_Dropdown_Quality != null && settings.m_Dropdown_Quality.gameObject.activeInHierarchy;
		}
	}
}
