using System;
using System.Collections.Generic;
using ArcadiusModLoader;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArcadiusSupra
{
	// A gag: replaces chibis with a Toyota Supra from Model/supra.json (converted from toyota-supra/ by convert_model.py).
	// By default players wear it as an outfit from the wardrobe (Outfits); applyTo can force it on instead.
	// Edit Mods/Supra/config.json and press the reload key (F9) to apply changes without restarting.
	public sealed class SupraEntry : IMod
	{
		private const float ScanInterval = 0.5f;

		private const string OutfitId = "supra";

		// Chibi_Controller is looked up by name; only its GameObject is needed.
		private static readonly Type ControllerType = AccessTools.TypeByName("Chibi_Controller");

		private readonly List<SupraBody> _bodies = new List<SupraBody>();

		private readonly HashSet<Chibi_Visual> _unsupported = new HashSet<Chibi_Visual>();

		private ModContext _context;

		private string _applyTo;

		private SupraResources _resources;

		private bool _resourcesFailed;

		private float _nextScan;

		public void OnLoad(ModContext context)
		{
			_context = context;
			ReadSettings();
			Outfits.Register(OutfitId, "Toyota Supra", Icon);
			ModEvents.Update += Scan;
			ModEvents.ConfigReloaded += Reload;
			Application.onBeforeRender += Pose;
		}

		private void ReadSettings()
		{
			_applyTo = _context.Config.Get("applyTo", "outfit").Trim().ToLowerInvariant();
			if (_applyTo != "outfit" && _applyTo != "self" && _applyTo != "players" && _applyTo != "all")
			{
				_context.Log.Warning("applyTo must be outfit, self, players or all; using outfit.");
				_applyTo = "outfit";
			}
		}

		// Rebuilds every Supra from the new settings and model on the next scan.
		private void Reload()
		{
			foreach (SupraBody body in _bodies)
			{
				body.Remove();
			}
			_bodies.Clear();
			_unsupported.Clear();
			_resources?.Destroy();
			_resources = null;
			_resourcesFailed = false;
			ReadSettings();
		}

		private void Scan()
		{
			if (Time.unscaledTime < _nextScan)
			{
				return;
			}
			_nextScan = Time.unscaledTime + ScanInterval;
			_bodies.RemoveAll(b => !b.IsAlive);
			_unsupported.RemoveWhere(v => v == null);
			foreach (Chibi_Visual visual in Object.FindObjectsByType<Chibi_Visual>())
			{
				SupraBody body = _bodies.Find(b => b.Visual == visual);
				if (!Applies(visual))
				{
					if (body != null)
					{
						body.Remove();
						_bodies.Remove(body);
					}
					continue;
				}
				if (body != null)
				{
					body.HideOriginal();
					continue;
				}
				if (_unsupported.Contains(visual) || !LoadResources())
				{
					continue;
				}
				body = SupraBody.Create(visual, _resources);
				if (body == null)
				{
					_unsupported.Add(visual);
					continue;
				}
				_bodies.Add(body);
			}
		}

		private bool Applies(Chibi_Visual visual)
		{
			if (_applyTo == "all")
			{
				return true;
			}
			if (_applyTo == "outfit")
			{
				return Outfits.WornBy(visual) == OutfitId;
			}
			Component controller = ControllerType == null ? null : visual.GetComponentInParent(ControllerType);
			if (controller == null)
			{
				return false;
			}
			// Networked_PlayerSetup renames the player this client owns.
			return _applyTo == "players" || controller.gameObject.name == "Local Player";
		}

		private bool LoadResources()
		{
			if (_resources == null && !_resourcesFailed)
			{
				_resources = SupraResources.Load(_context.Directory, _context.Config.Get("length", 3f), _context.Log);
				_resourcesFailed = _resources == null;
			}
			return _resources != null;
		}

		private Sprite Icon()
		{
			return LoadResources() ? _resources.Icon : null;
		}

		private void Pose()
		{
			for (int i = _bodies.Count - 1; i >= 0; i--)
			{
				SupraBody body = _bodies[i];
				if (!body.IsAlive)
				{
					continue;
				}
				try
				{
					body.Pose();
				}
				catch (Exception ex)
				{
					_context.Log.Error("Posing the Supra failed; restoring the chibi.", ex);
					body.Remove();
					_unsupported.Add(body.Visual);
					_bodies.RemoveAt(i);
				}
			}
		}
	}
}
