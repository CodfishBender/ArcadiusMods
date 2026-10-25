using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ArcadiusModLoader
{
	public enum ModState
	{
		Loaded,
		Disabled,
		Failed
	}

	public sealed class ModInfo
	{
		public ModManifest Manifest;

		public string Directory;

		public ModState State;

		public string Error;

		public ModContext Context;
	}

	// Finds mods in the Mods folder, orders them by dependency and loads them.
	// Folders starting with "_" or "." are ignored (e.g. _dump).
	public static class ModManager
	{
		private static readonly ModLog Log = new ModLog("ModLoader");

		private static readonly List<ModInfo> Mods = new List<ModInfo>();

		public static string ModsDirectory { get; private set; }

		public static IReadOnlyList<ModInfo> All => Mods;

		// Folders of loaded mods, in load order (for asset replacements).
		public static IEnumerable<string> LoadedDirectories => Mods.Where(m => m.State == ModState.Loaded).Select(m => m.Directory);

		public static void LoadAll(string modsDirectory)
		{
			ModsDirectory = modsDirectory;
			Directory.CreateDirectory(modsDirectory);
			AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

			List<ModInfo> found = Discover(modsDirectory);
			foreach (ModInfo mod in Order(found))
			{
				Mods.Add(mod);
				// A dependency may have failed while loading.
				ModInfo failedDependency = Mods.FirstOrDefault(m => m.State != ModState.Loaded
					&& mod.Manifest.dependsOn.Contains(m.Manifest.id, StringComparer.OrdinalIgnoreCase));
				if (mod.State == ModState.Loaded && failedDependency != null)
				{
					Fail(mod, "needs mod '" + failedDependency.Manifest.id + "', which failed to load");
				}
				if (mod.State == ModState.Loaded)
				{
					Load(mod);
				}
			}
			foreach (ModInfo mod in found.Where(m => !Mods.Contains(m)))
			{
				Mods.Add(mod);
			}
			Log.Info("Mods: " + string.Join(", ", Mods.Select(m => m.Manifest.id + " " + m.Manifest.version + " (" + m.State.ToString().ToLowerInvariant() + ")")));
		}

		private static List<ModInfo> Discover(string modsDirectory)
		{
			List<ModInfo> mods = new List<ModInfo>();
			foreach (string directory in Directory.GetDirectories(modsDirectory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
			{
				string folder = Path.GetFileName(directory);
				if (folder.StartsWith("_") || folder.StartsWith("."))
				{
					continue;
				}
				string manifestPath = Path.Combine(directory, "mod.json");
				if (!File.Exists(manifestPath))
				{
					Log.Warning("Skipping " + folder + ": no mod.json.");
					continue;
				}
				ModManifest manifest;
				try
				{
					manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<ModManifest>(File.ReadAllText(manifestPath));
				}
				catch (Exception ex)
				{
					Log.Error("Skipping " + folder + ": mod.json is invalid. " + ex.Message);
					continue;
				}
				manifest.id = string.IsNullOrWhiteSpace(manifest.id) ? folder : manifest.id.Trim();
				manifest.name = string.IsNullOrWhiteSpace(manifest.name) ? manifest.id : manifest.name;
				manifest.dependsOn = manifest.dependsOn ?? new List<string>();
				manifest.conflictsWith = manifest.conflictsWith ?? new List<string>();
				ModInfo mod = new ModInfo { Manifest = manifest, Directory = directory, State = manifest.enabled ? ModState.Loaded : ModState.Disabled };
				if (mods.Any(m => string.Equals(m.Manifest.id, manifest.id, StringComparison.OrdinalIgnoreCase)))
				{
					Fail(mod, "another mod already uses the id '" + manifest.id + "'");
				}
				mods.Add(mod);
			}
			return mods;
		}

		// Dependency order (stable by folder name). Mods with missing, disabled or circular
		// dependencies, or an enabled conflicting mod, are marked failed.
		private static List<ModInfo> Order(List<ModInfo> mods)
		{
			// Prefers a mod that isn't failed when ids are duplicated.
			ModInfo Find(string id) => mods.Where(m => string.Equals(m.Manifest.id, id, StringComparison.OrdinalIgnoreCase))
				.OrderBy(m => m.State == ModState.Failed).FirstOrDefault();

			// The mod declaring the conflict is the one that doesn't load.
			foreach (ModInfo mod in mods.Where(m => m.State == ModState.Loaded))
			{
				ModInfo conflict = mod.Manifest.conflictsWith.Select(Find).FirstOrDefault(m => m != null && m.State == ModState.Loaded);
				if (conflict != null)
				{
					Fail(mod, "can't run alongside '" + conflict.Manifest.id + "'; disable one of them in its mod.json");
				}
			}

			List<ModInfo> ordered = new List<ModInfo>();
			HashSet<ModInfo> visiting = new HashSet<ModInfo>();

			bool Visit(ModInfo mod)
			{
				if (ordered.Contains(mod))
				{
					return mod.State == ModState.Loaded;
				}
				if (!visiting.Add(mod))
				{
					Fail(mod, "circular dependency");
					return false;
				}
				foreach (string id in mod.Manifest.dependsOn)
				{
					ModInfo dependency = Find(id);
					if (dependency == null || dependency.State != ModState.Loaded || !Visit(dependency))
					{
						if (mod.State == ModState.Loaded)
						{
							Fail(mod, "needs mod '" + id + "', which is " + (dependency == null ? "missing" : "not loaded"));
						}
					}
				}
				visiting.Remove(mod);
				ordered.Add(mod);
				return mod.State == ModState.Loaded;
			}

			foreach (ModInfo mod in mods)
			{
				if (mod.State != ModState.Disabled)
				{
					Visit(mod);
				}
			}
			return ordered;
		}

		private static void Load(ModInfo mod)
		{
			mod.Context = new ModContext(mod.Manifest, mod.Directory);
			if (string.IsNullOrEmpty(mod.Manifest.dll))
			{
				return;
			}
			try
			{
				Assembly assembly = Assembly.LoadFrom(Path.Combine(mod.Directory, mod.Manifest.dll));
				Type[] entryTypes = assembly.GetTypes().Where(t => typeof(IMod).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface).ToArray();
				if (entryTypes.Length == 0)
				{
					mod.Context.Log.Warning(mod.Manifest.dll + " has no IMod class; nothing to run.");
				}
				foreach (Type type in entryTypes)
				{
					((IMod)Activator.CreateInstance(type)).OnLoad(mod.Context);
				}
				mod.Context.Log.Info("Loaded " + mod.Manifest.name + " " + mod.Manifest.version + ".");
			}
			catch (Exception ex)
			{
				mod.Context.Harmony.UnpatchAll(mod.Context.Harmony.Id);
				Fail(mod, "failed to load: " + (ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex));
			}
		}

		private static void Fail(ModInfo mod, string reason)
		{
			mod.State = ModState.Failed;
			mod.Error = reason;
			Log.Error("Mod '" + mod.Manifest.id + "' " + reason);
		}

		// Mod DLLs reference the loader, Harmony and each other by name; resolve those to what's loaded,
		// then to DLLs in mod folders.
		private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
		{
			string name = new AssemblyName(args.Name).Name;
			Assembly loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
			if (loaded != null)
			{
				return loaded;
			}
			foreach (ModInfo mod in Mods)
			{
				string path = Path.Combine(mod.Directory, name + ".dll");
				if (File.Exists(path))
				{
					return Assembly.LoadFrom(path);
				}
			}
			return null;
		}
	}
}
