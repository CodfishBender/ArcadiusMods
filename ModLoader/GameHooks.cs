using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ArcadiusModLoader
{
	// Ready-made Arcadius hooks, so common tweaks need no patching code.
	// Modifiers run in mod load order, each receiving the previous one's result.
	// A modifier that throws is logged and removed.
	public static class GameHooks
	{
		private static readonly ModLog Log = new ModLog("GameHooks");

		private static readonly Harmony Harmony = new Harmony("arcadius.modloader.gamehooks");

		// ResearchService sell-bonus method -> the item category it prices.
		private static readonly Dictionary<string, ResearchBonusType> SellMethods = new Dictionary<string, ResearchBonusType>
		{
			{ "ApplyOreSellBonus", ResearchBonusType.OreSellValue },
			{ "ApplyIngotSellBonus", ResearchBonusType.IngotSellValue },
			{ "ApplyFishSellBonus", ResearchBonusType.FishSellValue },
			{ "ApplyWoodSellBonus", ResearchBonusType.WoodSellValue },
			{ "ApplyCropSellBonus", ResearchBonusType.CropSellValue }
		};

		private static readonly List<Func<int, ResearchBonusType, int>> SellModifiers = new List<Func<int, ResearchBonusType, int>>();

		private static readonly List<Func<int, ResearchBonusType, int>> XpModifiers = new List<Func<int, ResearchBonusType, int>>();

		private static bool _sellPatched;

		private static bool _xpPatched;

		// Sell value of an ore, ingot, fish, wood or crop item, after research bonuses
		// (category: OreSellValue, IngotSellValue, FishSellValue, WoodSellValue or CropSellValue).
		// Also used for the value shown in item tooltips. Tools are not covered.
		public static void AddSellValueModifier(Func<int, ResearchBonusType, int> modifier)
		{
			if (!_sellPatched)
			{
				_sellPatched = true;
				HarmonyMethod postfix = new HarmonyMethod(typeof(GameHooks).GetMethod(nameof(SellValuePostfix), BindingFlags.NonPublic | BindingFlags.Static));
				foreach (string name in SellMethods.Keys)
				{
					Harmony.Patch(AccessTools.Method(typeof(ResearchService), name, new[] { typeof(int) }), postfix: postfix);
				}
			}
			SellModifiers.Add(modifier);
		}

		// XP gained in a skill, after research bonuses (skill: e.g. FishingXpGain, MiningXpGain).
		public static void AddXpModifier(Func<int, ResearchBonusType, int> modifier)
		{
			if (!_xpPatched)
			{
				_xpPatched = true;
				Harmony.Patch(AccessTools.Method(typeof(ResearchService), nameof(ResearchService.ApplyXpBonus), new[] { typeof(int), typeof(ResearchBonusType) }),
					postfix: new HarmonyMethod(typeof(GameHooks).GetMethod(nameof(XpPostfix), BindingFlags.NonPublic | BindingFlags.Static)));
			}
			XpModifiers.Add(modifier);
		}

		private static void SellValuePostfix(MethodBase __originalMethod, ref int __result)
		{
			__result = Apply(SellModifiers, __result, SellMethods[__originalMethod.Name], "sell value");
		}

		private static void XpPostfix(ResearchBonusType skillBonusType, ref int __result)
		{
			__result = Apply(XpModifiers, __result, skillBonusType, "XP");
		}

		private static int Apply(List<Func<int, ResearchBonusType, int>> modifiers, int value, ResearchBonusType type, string hook)
		{
			for (int i = 0; i < modifiers.Count; i++)
			{
				try
				{
					value = modifiers[i](value, type);
				}
				catch (Exception ex)
				{
					Log.Error("A " + hook + " modifier (" + modifiers[i].Method.DeclaringType + "." + modifiers[i].Method.Name + ") failed and was removed.", ex);
					modifiers.RemoveAt(i--);
				}
			}
			return value;
		}
	}
}
