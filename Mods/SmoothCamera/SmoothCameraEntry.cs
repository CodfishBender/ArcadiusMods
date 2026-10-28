using System.Reflection;
using Arcadius.Player;
using ArcadiusModLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ArcadiusSmoothCamera
{
	// Player_Camera.UpdateCameraTransform (run from LateUpdate) puts the camera exactly behind the player every
	// frame, but the player only moves in Chibi_Controller.FixedUpdate, 50 times a second. Above 50 FPS the view
	// then stands still on some frames and jumps on others. This replaces it with the same camera, plus:
	// - interpolation: the player's model and the camera are drawn between the last two physics steps (one step
	//   behind), like Rigidbody interpolation. The player's real position and movement are unchanged.
	// - rotationSmoothing: the view eases towards the mouse or stick angles. Movement still uses the raw angles.
	// - heightSmoothing: the camera eases up and down over steps and landings, never lagging more than MaxHeightLag.
	// - zoomOutSmoothing: when a wall stops blocking the camera it glides back out. It still moves in at once, so
	//   it doesn't go through walls.
	// Each setting is the time constant of an exponential ease in seconds; 0 turns it off.
	public sealed class SmoothCameraEntry : IMod
	{
		// The game's values in Player_Camera.
		private const float CameraDistance = 6f;

		private const float PivotHeight = 1.7f;

		private const float MaxHeightLag = 0.25f;

		private static ModContext _context;

		private static bool _interpolation;

		private static float _rotationSmoothing;

		private static float _heightSmoothing;

		private static float _zoomOutSmoothing;

		// Runtime is internal and Chibi_Controller is a NetworkBehaviour, so both are reached by reflection.
		private static FieldInfo _activeChibi;

		private static FieldInfo _chibiTransform;

		private static FieldInfo _chibiMovement;

		private static MethodInfo _setModelVisibility;

		private static readonly object[] Visible = { true };

		private static readonly object[] Hidden = { false };

		private static Transform _root;

		private static Component _movement;

		private static Transform _visual;

		private static Vector3 _visualOffset;

		private static Quaternion _visualRotation;

		// The player's pose after the last two physics steps.
		private static Vector3 _previousPosition;

		private static Quaternion _previousRotation;

		private static Vector3 _currentPosition;

		private static Quaternion _currentRotation;

		private static float _lastCameraTime = float.NegativeInfinity;

		private static Vector3 _pivot;

		private static float _yaw;

		private static float _pitch;

		private static float _distance;

		public void OnLoad(ModContext context)
		{
			_context = context;
			ReadSettings();
			ModEvents.ConfigReloaded += ReadSettings;
			_activeChibi = AccessTools.Field(AccessTools.TypeByName("Arcadius.Player.Runtime"), "activeChibi");
			_chibiTransform = AccessTools.Field(_activeChibi.FieldType, "transform");
			_chibiMovement = AccessTools.Field(_activeChibi.FieldType, "movement");
			_setModelVisibility = AccessTools.Method(_chibiMovement.FieldType, "SetModelVisibility");
			Harmony harmony = context.Harmony;
			harmony.Patch(AccessTools.Method(typeof(Player_Camera), "UpdateCameraTransform"),
				prefix: new HarmonyMethod(typeof(SmoothCameraEntry), nameof(BeforeUpdateCameraTransform)));
			harmony.Patch(AccessTools.Method("Chibi_Controller:FixedUpdate"),
				postfix: new HarmonyMethod(typeof(SmoothCameraEntry), nameof(AfterFixedUpdate)));
		}

		private static void ReadSettings()
		{
			ModConfig config = _context.Config;
			_interpolation = config.Get("interpolation", true);
			_rotationSmoothing = Mathf.Clamp(config.Get("rotationSmoothing", 0.03f), 0f, 1f);
			_heightSmoothing = Mathf.Clamp(config.Get("heightSmoothing", 0.06f), 0f, 1f);
			_zoomOutSmoothing = Mathf.Clamp(config.Get("zoomOutSmoothing", 0.3f), 0f, 2f);
		}

		public static void AfterFixedUpdate(Component __instance)
		{
			if (!ReferenceEquals(__instance, _movement) || _root == null)
			{
				return;
			}
			_previousPosition = _currentPosition;
			_previousRotation = _currentRotation;
			_currentPosition = _root.position;
			_currentRotation = _root.rotation;
			// Until the camera places it again, keep the model on the real player (e.g. while the fly camera is on).
			PlaceVisual(_currentPosition, _currentRotation);
		}

		public static bool BeforeUpdateCameraTransform(Camera ___m_Camera, LayerMask ___m_Layer_Avoid,
			float ___rotation_Horizontal, float ___rotation_Vertical)
		{
			if (!FindPlayer())
			{
				return true;
			}
			float deltaTime = Time.deltaTime;
			// The first frame, or the camera was off for a while (fly camera): start from the game's own view.
			bool snap = Time.unscaledTime - _lastCameraTime > 0.25f;
			_lastCameraTime = Time.unscaledTime;

			Vector3 position = _root.position;
			Quaternion rotation = _root.rotation;
			if (snap || position != _currentPosition || rotation != _currentRotation)
			{
				// Moved outside a physics step (teleport, respawn): nothing to interpolate from.
				_previousPosition = _currentPosition = position;
				_previousRotation = _currentRotation = rotation;
			}
			if (_interpolation)
			{
				float t = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
				position = Vector3.Lerp(_previousPosition, _currentPosition, t);
				rotation = Quaternion.Slerp(_previousRotation, _currentRotation, t);
			}
			PlaceVisual(position, rotation);

			Vector3 target = position + new Vector3(0f, PivotHeight, 0f);
			if (snap)
			{
				_pivot = target;
				_yaw = ___rotation_Horizontal;
				_pitch = ___rotation_Vertical;
				_distance = CameraDistance;
			}
			_pivot.x = target.x;
			_pivot.z = target.z;
			_pivot.y = Mathf.Clamp(Mathf.Lerp(_pivot.y, target.y, Ease(_heightSmoothing, deltaTime)),
				target.y - MaxHeightLag, target.y + MaxHeightLag);
			_yaw = Mathf.Lerp(_yaw, ___rotation_Horizontal, Ease(_rotationSmoothing, deltaTime));
			_pitch = Mathf.Lerp(_pitch, ___rotation_Vertical, Ease(_rotationSmoothing, deltaTime));
			Quaternion look = Quaternion.Euler(_pitch, _yaw, 0f);

			float free = Physics.SphereCast(_pivot, 0.25f, look * Vector3.back, out RaycastHit hit, CameraDistance,
				___m_Layer_Avoid) ? hit.distance : CameraDistance;
			_distance = free < _distance ? free : Mathf.Lerp(_distance, free, Ease(_zoomOutSmoothing, deltaTime));

			// The rest is the game's own: hide the model when the camera is inside or right above or below it, and
			// show the underwater overlay when looking up at water from below.
			bool visible = _distance > 1f && _pitch > -60f && (_distance >= 1.5f || _pitch <= 60f);
			_setModelVisibility.Invoke(_movement, visible ? Visible : Hidden);
			Vector3 cameraPosition = _pivot + look * Vector3.back * _distance;
			___m_Camera.transform.SetPositionAndRotation(cameraPosition, look);
			Image underwater = UserInterfaceBehaviour.singleton.m_UnderWaterImageFill;
			Vector3 direction = look * Vector3.forward;
			if (direction.y < 0f)
			{
				underwater.enabled = false;
				return false;
			}
			underwater.enabled = Physics.Raycast(cameraPosition, direction, out RaycastHit waterHit,
				float.PositiveInfinity, -1, QueryTriggerInteraction.Collide)
				&& waterHit.transform.GetComponent<Entity_Water>() != null;
			underwater.fillAmount = 1f;
			return false;
		}

		private static bool FindPlayer()
		{
			if (_root != null && _movement != null)
			{
				return true;
			}
			object chibi = _activeChibi.GetValue(null);
			_root = (Transform)_chibiTransform.GetValue(chibi);
			_movement = (Component)_chibiMovement.GetValue(chibi);
			_visual = null;
			_lastCameraTime = float.NegativeInfinity;
			if (_root == null || _movement == null)
			{
				return false;
			}
			// VisualChibi, the model under the physics object, is what gets interpolated.
			Chibi_Visual visual = _root.GetComponentInChildren<Chibi_Visual>(true);
			if (visual != null && visual.transform != _root)
			{
				_visual = visual.transform;
				_visualOffset = _root.InverseTransformPoint(_visual.position);
				_visualRotation = Quaternion.Inverse(_root.rotation) * _visual.rotation;
			}
			return true;
		}

		private static void PlaceVisual(Vector3 position, Quaternion rotation)
		{
			if (_visual != null)
			{
				_visual.SetPositionAndRotation(position + rotation * Vector3.Scale(_visualOffset, _root.lossyScale),
					rotation * _visualRotation);
			}
		}

		private static float Ease(float timeConstant, float deltaTime)
		{
			return timeConstant <= 0f ? 1f : 1f - Mathf.Exp(-deltaTime / timeConstant);
		}
	}
}
