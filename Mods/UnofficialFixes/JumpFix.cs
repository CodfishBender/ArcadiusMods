using HarmonyLib;

namespace ArcadiusUnofficialFixes
{
	// Chibi_Controller.FixedUpdate only takes a jump on the frame the key goes down (input_Jump && !wasJumpPressed),
	// so holding space jumps once. Clearing wasJumpPressed while it's held makes every physics step count as a
	// press: the game's jump buffer then jumps again as soon as the player lands. Jumping still needs the ground
	// (or the game's coyote time), so holding it in the air doesn't double jump.
	internal static class JumpFix
	{
		public static void Patch(Harmony harmony)
		{
			// Chibi_Controller is a NetworkBehaviour, so it's named rather than referenced.
			harmony.Patch(AccessTools.Method("Chibi_Controller:FixedUpdate"),
				prefix: new HarmonyMethod(typeof(JumpFix), nameof(BeforeFixedUpdate)));
		}

		public static void BeforeFixedUpdate(bool ___input_Jump, ref bool ___wasJumpPressed)
		{
			if (UnofficialFixesEntry.HoldToJump && ___input_Jump)
			{
				___wasJumpPressed = false;
			}
		}
	}
}
