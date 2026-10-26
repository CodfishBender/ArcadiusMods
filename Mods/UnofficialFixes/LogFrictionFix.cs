using HarmonyLib;
using UnityEngine;

namespace ArcadiusUnofficialFixes
{
	// Wood pickups (the logs a felled tree drops) are capsules with no damping (0 linear, 0.05 angular) and no
	// physics material, thrown by an explosion force in Pickup_Wood.Start. Once one lands on its side it rolls
	// down any slope and keeps going: PhysX has no rolling resistance, so only angular damping stops it. This
	// gives each pickup damping and a grippy material on its solid collider (not the pickup trigger). The tree's
	// falling log (Log) is left alone, so it still topples at full speed before breaking into pickups.
	internal static class LogFrictionFix
	{
		private static PhysicsMaterial _material;

		public static void Patch(Harmony harmony)
		{
			harmony.Patch(AccessTools.Method(typeof(Pickup_Wood), "Start"),
				postfix: new HarmonyMethod(typeof(LogFrictionFix), nameof(AfterStart)));
		}

		public static void AfterStart(Pickup_Wood __instance)
		{
			if (!UnofficialFixesEntry.LogFriction)
			{
				return;
			}
			Rigidbody body = __instance.GetComponent<Rigidbody>();
			if (body != null)
			{
				body.linearDamping = Mathf.Max(body.linearDamping, UnofficialFixesEntry.LogDrag);
				body.angularDamping = Mathf.Max(body.angularDamping, UnofficialFixesEntry.LogRollingDrag);
			}
			if (_material == null)
			{
				_material = new PhysicsMaterial("LogFriction")
				{
					staticFriction = 1f,
					dynamicFriction = 0.9f,
					bounciness = 0f,
					frictionCombine = PhysicsMaterialCombine.Maximum,
					bounceCombine = PhysicsMaterialCombine.Minimum
				};
			}
			foreach (Collider collider in __instance.GetComponentsInChildren<Collider>())
			{
				if (!collider.isTrigger)
				{
					collider.sharedMaterial = _material;
				}
			}
		}
	}
}
