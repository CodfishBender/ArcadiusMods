using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Usage: Patcher <original Assembly-CSharp.dll> <ArcadiusOffline.dll> <output Assembly-CSharp.dll> <game Managed dir> [offline|capture]
// offline (default): play without online services. capture: stay online, log all Cloud Code traffic.
if (args.Length < 4 || args.Length > 5 || (args.Length == 5 && args[4] != "offline" && args[4] != "capture"))
{
	Console.Error.WriteLine("Usage: Patcher <original Assembly-CSharp.dll> <ArcadiusOffline.dll> <output dll> <game Managed dir> [offline|capture]");
	return 2;
}
bool capture = args.Length == 5 && args[4] == "capture";

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));
resolver.AddSearchDirectory(args[3]);

using var game = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { AssemblyResolver = resolver });
using var mod = AssemblyDefinition.ReadAssembly(args[1], new ReaderParameters { AssemblyResolver = resolver });
var module = game.MainModule;
MethodReference Mod(string name, string modType = "OfflineMode") =>
	module.ImportReference(mod.MainModule.GetType("ArcadiusOffline." + modType).Methods.Single(m => m.Name == name));

TypeDefinition Type(string fullName) =>
	module.GetType(fullName) ?? throw new Exception("Type not found: " + fullName);

MethodDefinition Method(string type, string name) =>
	Type(type).Methods.Single(m => m.Name == name);

int patched = 0;

// Replace a method's whole body.
void Replace(MethodDefinition method, Action<ILProcessor> emit)
{
	var body = method.Body;
	body.Instructions.Clear();
	body.ExceptionHandlers.Clear();
	body.Variables.Clear();
	body.InitLocals = false;
	emit(body.GetILProcessor());
	patched++;
	Console.WriteLine("  replaced " + method.DeclaringType.FullName + "::" + method.Name);
}

// Body becomes: load every argument, call the mod method (optionally generic), return.
void Forward(string type, string name, string modMethod, Func<MethodDefinition, TypeReference> genericArg = null, string modType = "OfflineMode")
{
	var method = Method(type, name);
	MethodReference target = Mod(modMethod, modType);
	if (genericArg != null)
	{
		var instance = new GenericInstanceMethod(target);
		instance.GenericArguments.Add(genericArg(method));
		target = instance;
	}
	Replace(method, il =>
	{
		for (int i = 0; i < target.Parameters.Count; i++)
		{
			il.Emit(OpCodes.Ldarg, method.Parameters[i]);
		}
		il.Emit(OpCodes.Call, target);
		il.Emit(OpCodes.Ret);
	});
}

// Body becomes: return OfflineMode.Unavailable<T>(feature), T taken from the Task<T> return type.
void Unavailable(string type, string name, string feature)
{
	var method = Method(type, name);
	var instance = new GenericInstanceMethod(Mod("Unavailable"));
	instance.GenericArguments.Add(((GenericInstanceType)method.ReturnType).GenericArguments[0]);
	Replace(method, il =>
	{
		il.Emit(OpCodes.Ldstr, feature);
		il.Emit(OpCodes.Call, instance);
		il.Emit(OpCodes.Ret);
	});
}

// Pass a method's int return value through a mod method: `return Hook(result)`, or `return Hook(result, arg1)`.
// Each ret becomes the hook call itself, so branches that targeted the ret still go through the hook.
void Postfix(MethodDefinition method, string modMethod, string modType, bool passSecondArg)
{
	var il = method.Body.GetILProcessor();
	var hook = Mod(modMethod, modType);
	foreach (var ret in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList())
	{
		var last = ret;
		if (passSecondArg)
		{
			ret.OpCode = OpCodes.Ldarg_1;
			ret.Operand = null;
			last = il.Create(OpCodes.Call, hook);
			il.InsertAfter(ret, last);
		}
		else
		{
			ret.OpCode = OpCodes.Call;
			ret.Operand = hook;
		}
		il.InsertAfter(last, il.Create(OpCodes.Ret));
	}
	patched++;
	Console.WriteLine("  postfix  " + method.DeclaringType.FullName + "::" + method.Name + " -> " + modType + "." + modMethod);
}

Console.WriteLine("Patching " + args[0] + (capture ? " (capture build)" : " (offline build)"));

if (capture)
{
	Forward("UnityServicesGateway", "CallCloudCodeAsync", "CallCloudCode", m => m.GenericParameters[0], "CloudCodeCapture");
	game.Write(args[2]);
	Console.WriteLine($"Wrote {args[2]} ({patched} patches)");
	return 0;
}

// Unity Gaming Services: no sign-in, local saves, stubbed Cloud Code.
// IsSignedIn stays false so account linking, Vivox chat and UGS name lookups stay idle.
Forward("UnityServicesGateway", "InitializeAndSignInAsync", "Completed");
Forward("UnityServicesGateway", "get_PlayerId", "get_PlayerId");
Forward("UnityServicesGateway", "LoadPlayerDataAsync", "LoadPlayerData");
Forward("UnityServicesGateway", "SavePlayerDataAsync", "SavePlayerData");
Forward("UnityServicesGateway", "CallCloudCodeAsync", "CallCloudCode", m => m.GenericParameters[0]);
Forward("UnityServicesGateway", "PushLeaderboardScoreAsync", "Completed");
Unavailable("UnityServicesGateway", "GetLeaderboardScoresAsync", "Leaderboards");
Unavailable("UnityServicesGateway", "GetPlayerRangeAsync", "Leaderboards");
Unavailable("UnityServicesGateway", "GetLeaderboardsServiceAsync", "Leaderboards");

// Mining contracts: served locally from captured server definitions.
Forward("MiningContractService", "CallAsync", "Call", modType: "OfflineContracts");

// Upkeep stations: served locally from captured server definitions.
Forward("UpkeepStationService", "CallAsync", "Call", modType: "OfflineUpkeep");

// Town upkeep bonuses (sell value, Fishing XP): shown by the client but never applied; apply them.
foreach (var sell in new[] { "ApplyOreSellBonus", "ApplyIngotSellBonus", "ApplyFishSellBonus", "ApplyWoodSellBonus", "ApplyCropSellBonus" })
{
	Postfix(Method("ResearchService", sell), "ApplyTownSellBonus", "OfflineUpkeep", passSecondArg: false);
}
Postfix(Type("ResearchService").Methods.Single(m => m.Name == "ApplyXpBonus" && m.Parameters.Count == 2),
	"ApplyTownXpBonus", "OfflineUpkeep", passSecondArg: true);

// Display names: the UGS account name can't be updated offline; skip the sync so the change succeeds.
Forward("DisplayNameService", "SyncUgsPlayerNameAsync", "Completed");
{
	// Offline allows unlimited changes (see OfflineMode.SetDisplayName); fix the UI hint to match.
	var hint = Method("BagHandler", "RefreshDisplayNameUI").Body.Instructions.Single(i =>
		i.OpCode == OpCodes.Ldstr && (string)i.Operand == "You can change your display name once.");
	hint.Operand = "You can change your display name any time while offline.";
	patched++;
	Console.WriteLine("  reworded BagHandler::RefreshDisplayNameUI hint");
}

// Steam -> UGS cosmetic migration needs UGS; skip it.
Forward("SteamLegacyCosmeticService", "LinkSteamAndLogArcadiusCosmeticsAsync", "Completed");

// Dedicated-server heartbeat: nothing to report to.
Replace(Method("UgsServerHeartbeat", "Update"), il => il.Emit(OpCodes.Ret));

// Server select: start an in-process server right before connecting.
{
	var connect = Method("Menu_ServerSelect", "ConnectSelectedServer");
	var il = connect.Body.GetILProcessor();
	var ensureInstalled = connect.Body.Instructions.Single(i =>
		i.OpCode == OpCodes.Call && i.Operand is MethodReference m && m.Name == "EnsureInstalled");
	if (ensureInstalled.Next.OpCode != OpCodes.Pop)
	{
		throw new Exception("Unexpected IL after UgsAccessAuthenticator.EnsureInstalled");
	}
	// Insert after the pop: the branch that skips the "NetworkManager missing" path targets the call itself.
	il.InsertAfter(ensureInstalled.Next, il.Create(OpCodes.Call, Mod("EnsureLocalServer")));
	patched++;
	Console.WriteLine("  hooked   Menu_ServerSelect::ConnectSelectedServer");
}

// Connecting menu: continue to server select if it is re-shown after sign-in already finished.
{
	var onEnable = Method("Menu_Connecting", "OnEnable");
	var il = onEnable.Body.GetILProcessor();
	var first = onEnable.Body.Instructions[0];
	il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
	il.InsertBefore(first, il.Create(OpCodes.Call, Mod("OnConnectingMenuEnabled")));
	patched++;
	Console.WriteLine("  hooked   Menu_Connecting::OnEnable");
}

game.Write(args[2]);
Console.WriteLine($"Wrote {args[2]} ({patched} patches)");
return 0;
