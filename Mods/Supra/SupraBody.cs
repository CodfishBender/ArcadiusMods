using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArcadiusSupra
{
	// One chibi turned into a Toyota Supra. The car sits where the chibi stands and faces where it faces; the wheels
	// roll with the distance driven, and the body pitches when speeding up or braking and leans out of turns.
	internal sealed class SupraBody
	{
		// Degrees of pitch per m/s² of acceleration, and of lean per m/s² sideways, and the most of either.
		private const float PitchPerAcceleration = 1.2f;

		private const float LeanPerAcceleration = 1.5f;

		private const float MaxTilt = 8f;

		// How quickly the tilt follows (per second).
		private const float TiltResponse = 6f;

		// Moving further than this in a frame (respawn, teleport) doesn't count as driving.
		private const float TeleportDistance = 5f;

		private readonly List<Renderer> _hidden = new List<Renderer>();

		private readonly List<(Transform transform, float radius)> _wheels = new List<(Transform, float)>();

		private Transform _model;

		private GameObject _root;

		private Transform _body;

		private Vector3 _pivot;

		private Vector3 _lastPosition;

		private Vector3 _lastForward;

		private Vector3 _velocity;

		private float _pitch;

		private float _lean;

		private bool _moved;

		private SupraBody()
		{
		}

		public Chibi_Visual Visual { get; private set; }

		public bool IsAlive => Visual != null && _model != null && _root != null;

		public static SupraBody Create(Chibi_Visual visual, SupraResources resources)
		{
			Transform hips = FindChild(visual.transform, "hips");
			Transform model = hips != null && hips.parent != null ? hips.parent.parent : visual.transform;
			Renderer head = FindChild(model, "Model_Head")?.GetComponent<Renderer>();
			Material template = head != null ? head.sharedMaterial : null;
			int layer = head != null ? head.gameObject.layer : visual.gameObject.layer;

			SupraBody body = new SupraBody { Visual = visual, _model = model, _pivot = resources.Pivot };
			body._root = new GameObject("Supra") { layer = layer };
			body._root.transform.SetParent(model, false);

			GameObject bodyObject = new GameObject("Supra body") { layer = layer };
			bodyObject.transform.SetParent(body._root.transform, false);
			bodyObject.AddComponent<MeshFilter>().sharedMesh = resources.Body;
			Material[] materials = new Material[resources.BodyColours.Length];
			for (int i = 0; i < materials.Length; i++)
			{
				materials[i] = resources.GetMaterial(template, resources.BodyColours[i]);
			}
			bodyObject.AddComponent<MeshRenderer>().sharedMaterials = materials;
			body._body = bodyObject.transform;

			foreach (SupraResources.Wheel wheel in resources.Wheels)
			{
				GameObject wheelObject = new GameObject("Supra wheel") { layer = layer };
				wheelObject.transform.SetParent(body._root.transform, false);
				wheelObject.transform.localPosition = wheel.Centre;
				wheelObject.AddComponent<MeshFilter>().sharedMesh = wheel.Mesh;
				wheelObject.AddComponent<MeshRenderer>().sharedMaterial = resources.GetMaterial(template, wheel.Colour);
				body._wheels.Add((wheelObject.transform, wheel.Radius));
			}

			body.HideOriginal();
			return body;
		}

		// Hides the chibi's meshes and held tools, including ones the game switches on later.
		public void HideOriginal()
		{
			foreach (Renderer renderer in _model.GetComponentsInChildren<Renderer>(true))
			{
				if (renderer.enabled && !renderer.transform.IsChildOf(_root.transform))
				{
					renderer.enabled = false;
					_hidden.Add(renderer);
				}
			}
		}

		// Runs just before rendering.
		public void Pose()
		{
			float dt = Time.deltaTime;
			Vector3 position = _model.position;
			Vector3 forward = _model.forward;
			Vector3 moved = position - _lastPosition;
			if (!_moved || moved.sqrMagnitude > TeleportDistance * TeleportDistance)
			{
				_moved = true;
				_lastPosition = position;
				_lastForward = forward;
				_velocity = Vector3.zero;
				return;
			}
			_lastPosition = position;
			if (dt <= 0f)
			{
				return;
			}

			// Wheels roll with the distance driven forwards (backwards rolls them back).
			float driven = Vector3.Dot(moved, forward);
			foreach ((Transform wheel, float radius) in _wheels)
			{
				wheel.Rotate(Vector3.right, driven / Mathf.Max(0.01f, radius) * Mathf.Rad2Deg, Space.Self);
			}

			// Flat acceleration in the car's frame: forwards for pitch, sideways (turning) for lean.
			Vector3 velocity = Vector3.ProjectOnPlane(moved / dt, Vector3.up);
			Vector3 acceleration = (velocity - _velocity) / dt;
			_velocity = velocity;
			float yawRate = Vector3.SignedAngle(_lastForward, forward, Vector3.up) * Mathf.Deg2Rad / dt;
			_lastForward = forward;
			float sideways = yawRate * Vector3.Dot(velocity, forward);
			float follow = 1f - Mathf.Exp(-TiltResponse * dt);
			_pitch = Mathf.Lerp(_pitch, Mathf.Clamp(-Vector3.Dot(acceleration, forward) * PitchPerAcceleration, -MaxTilt, MaxTilt), follow);
			_lean = Mathf.Lerp(_lean, Mathf.Clamp(sideways * LeanPerAcceleration, -MaxTilt, MaxTilt), follow);
			// Tilt the body (not the wheels) about the middle of the wheelbase.
			Quaternion tilt = Quaternion.Euler(_pitch, 0f, _lean);
			_body.localRotation = tilt;
			_body.localPosition = _pivot - tilt * _pivot;
		}

		// Brings the chibi back.
		public void Remove()
		{
			foreach (Renderer renderer in _hidden)
			{
				if (renderer != null)
				{
					renderer.enabled = true;
				}
			}
			_hidden.Clear();
			if (_root != null)
			{
				Object.Destroy(_root);
			}
		}

		private static Transform FindChild(Transform parent, string name)
		{
			foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
			{
				if (child.name == name)
				{
					return child;
				}
			}
			return null;
		}
	}
}
