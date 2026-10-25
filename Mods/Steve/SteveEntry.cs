using System;
using System.Collections.Generic;
using ArcadiusModLoader;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArcadiusSteve
{
	// Replaces chibis with the Steve model from Model/steve.json (converted from minecraft-steve/source/steve.fbx).
	// Steve's parts follow the chibi's bones, so the game's animations, emotes and held tools keep working.
	// By default players wear it as an outfit from the wardrobe (Outfits); applyTo can force it on instead.
	// Edit Mods/Steve/config.json (or the skin PNG) and press the reload key (F9) to apply changes without restarting.
	public sealed class SteveEntry : IMod
	{
		private const float ScanInterval = 0.5f;

		private const string OutfitId = "steve";

		// Chibi_Controller is looked up by name; only its GameObject is needed.
		private static readonly Type ControllerType = AccessTools.TypeByName("Chibi_Controller");

		private readonly List<SteveBody> _bodies = new List<SteveBody>();

		private readonly HashSet<Chibi_Visual> _unsupported = new HashSet<Chibi_Visual>();

		private ModContext _context;

		private string _applyTo;

		private SteveResources _resources;

		private bool _resourcesFailed;

		private float _nextScan;

		private Sprite _icon;

		public void OnLoad(ModContext context)
		{
			_context = context;
			ReadSettings();
			Outfits.Register(OutfitId, "Steve", Icon);
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

		// Rebuilds every Steve from the new settings, skin and model on the next scan.
		private void Reload()
		{
			foreach (SteveBody body in _bodies)
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
				SteveBody body = _bodies.Find(b => b.Visual == visual);
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
				body = SteveBody.Create(visual, _resources, _context.Log);
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
				_resources = SteveResources.Load(_context.Directory, _context.Config.Get("skin", "Model/steve.png"),
					_context.Config.Get("height", 1.7f), _context.Log);
				_resourcesFailed = _resources == null;
			}
			return _resources != null;
		}

		// The skin's face (8x8 pixels at 8,8 from the top left); remade when the skin is reloaded.
		private Sprite Icon()
		{
			if (!LoadResources())
			{
				return null;
			}
			Texture2D skin = _resources.Skin;
			if (_icon == null || _icon.texture != skin)
			{
				_icon = Sprite.Create(skin, new Rect(8f, skin.height - 16f, 8f, 8f), new Vector2(0.5f, 0.5f), 8f);
			}
			return _icon;
		}

		private void Pose()
		{
			for (int i = _bodies.Count - 1; i >= 0; i--)
			{
				SteveBody body = _bodies[i];
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
					_context.Log.Error("Posing Steve failed; restoring the chibi.", ex);
					body.Remove();
					_unsupported.Add(body.Visual);
					_bodies.RemoveAt(i);
				}
			}
		}
	}
}
