using System;
using System.Collections.Generic;
using ArcadiusModLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArcadiusSteve
{
	// One chibi turned into Steve. Each Steve part copies the rotation its chibi bone has made since the bind
	// pose (a T-pose), around Steve's own joints, so Steve keeps Minecraft proportions while the game animates.
	internal sealed class SteveBody
	{
		private sealed class Part
		{
			public Transform Transform;

			public Transform Bone;

			// Legs aim from the hip at this bone (the foot) rather than along the thigh, since the chibi
			// stands with its knees bent and Steve's legs are straight.
			public Transform End;

			// Turns the bind-pose hip-to-foot line (the chibi's ankle is a little behind its hip) to straight down.
			public Quaternion Straighten = Quaternion.identity;

			public Quaternion InverseBind;

			// Turns the Steve part (limbs hang straight down) to the bone's bind direction.
			public Quaternion Align = Quaternion.identity;

			// Joint position relative to Steve's hips.
			public Vector3 Offset;
		}

		// Steve part, chibi bone it follows, whether it's a limb.
		private static readonly (string part, string bone, bool limb)[] Rig =
		{
			("body", "upper.chest", false),
			("head", "head", false),
			("arm.R", "upper.Arm.R", true),
			("arm.L", "upper.Arm.L", true),
			("leg.R", "thigh.R", true),
			("leg.L", "thigh.L", true),
		};

		private readonly List<Part> _limbs = new List<Part>();

		private readonly List<Renderer> _hidden = new List<Renderer>();

		private Transform _model;

		// The chibi's Animator culls by its renderers, which are hidden, so it's kept animating.
		private Animator _animator;

		private AnimatorCullingMode _cullingMode;

		private GameObject _root;

		private Part _body;

		private Vector3 _hips;

		// The game parents held tools here, under the chibi's right hand; it's moved to Steve's right hand.
		private Transform _equip;

		private Vector3 _equipPosition;

		private Quaternion _equipRotation;

		private Transform _rightArm;

		private Vector3 _hand;

		private SteveBody()
		{
		}

		public Chibi_Visual Visual { get; private set; }

		public bool IsAlive => Visual != null && _model != null && _root != null;

		public static SteveBody Create(Chibi_Visual visual, SteveResources resources, ModLog log)
		{
			Transform hips = FindChild(visual.transform, "hips");
			Transform model = hips != null && hips.parent != null ? hips.parent.parent : null;
			SkinnedMeshRenderer skinned = model == null ? null : FindSkinned(model);
			if (skinned == null)
			{
				log.Warning(visual.name + " has no chibi skeleton; leaving it as it is.");
				return null;
			}

			SteveBody body = new SteveBody { Visual = visual, _model = model };
			body._root = new GameObject("Steve");
			body._root.transform.SetParent(model, false);
			Renderer head = FindChild(model, "Model_Head")?.GetComponent<Renderer>();
			Material material = resources.GetMaterial(head != null ? head.sharedMaterial : null);
			int layer = head != null ? head.gameObject.layer : visual.gameObject.layer;

			// Bind pose of each bone in the model's space, from the chibi's skinned mesh.
			Matrix4x4 toModel = model.worldToLocalMatrix * skinned.transform.localToWorldMatrix;
			Matrix4x4[] bindposes = skinned.sharedMesh.bindposes;
			Transform[] bones = skinned.bones;
			Vector3 hipsPivot = resources.Parts["body"].Pivot;
			foreach ((string partName, string boneName, bool limb) in Rig)
			{
				int index = Array.FindIndex(bones, b => b != null && b.name == boneName);
				string endName = boneName.StartsWith("thigh.", StringComparison.Ordinal) ? "foot" + boneName.Substring(5) : null;
				Matrix4x4 bind = toModel * bindposes[index].inverse;
				SteveResources.Part mesh = resources.Parts[partName];
				GameObject partObject = new GameObject("Steve " + partName) { layer = layer };
				partObject.transform.SetParent(body._root.transform, false);
				partObject.AddComponent<MeshFilter>().sharedMesh = mesh.Mesh;
				partObject.AddComponent<MeshRenderer>().sharedMaterial = material;
				Part part = new Part
				{
					Transform = partObject.transform,
					Bone = bones[index],
					End = endName == null ? null : Array.Find(bones, b => b != null && b.name == endName),
					InverseBind = Quaternion.Inverse(Quaternion.LookRotation(bind.GetColumn(2), bind.GetColumn(1))),
					Offset = mesh.Pivot - hipsPivot,
				};
				if (part.End != null)
				{
					Matrix4x4 endBind = toModel * bindposes[Array.IndexOf(bones, part.End)].inverse;
					part.Straighten = Quaternion.FromToRotation(endBind.GetColumn(3) - bind.GetColumn(3), Vector3.down);
				}
				if (limb)
				{
					part.Align = Quaternion.FromToRotation(Vector3.down, ((Vector3)bind.GetColumn(1)).normalized);
				}
				if (partName == "body")
				{
					body._body = part;
					body._hips = hipsPivot;
				}
				else
				{
					body._limbs.Add(part);
				}
				if (partName == "arm.R")
				{
					body._rightArm = part.Transform;
					// Grip one pixel above the end of the arm.
					body._hand = new Vector3(0f, mesh.Min.y - mesh.Pivot.y + resources.Pixel, 0f);
				}
			}

			body._equip = FindChild(model, "Equip_RightHand");
			if (body._equip != null)
			{
				body._equipPosition = body._equip.localPosition;
				body._equipRotation = body._equip.localRotation;
			}
			body._animator = model.GetComponent<Animator>();
			if (body._animator != null)
			{
				body._cullingMode = body._animator.cullingMode;
				body._animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
			}
			body.HideOriginal();
			body.Pose();
			return body;
		}

		// Hides the chibi's meshes (head, hair, hats, clothes), including ones the game switches on later.
		public void HideOriginal()
		{
			foreach (Renderer renderer in _model.GetComponentsInChildren<Renderer>(true))
			{
				if (renderer.enabled && !renderer.transform.IsChildOf(_root.transform) && (_equip == null || !renderer.transform.IsChildOf(_equip)))
				{
					renderer.enabled = false;
					_hidden.Add(renderer);
				}
			}
		}

		// Runs after the Animator and LateUpdate, just before rendering.
		public void Pose()
		{
			Quaternion toModel = Quaternion.Inverse(_model.rotation);
			Quaternion bodyRotation = Delta(_body, toModel);
			_body.Transform.localPosition = _hips;
			_body.Transform.localRotation = bodyRotation;
			foreach (Part limb in _limbs)
			{
				limb.Transform.localPosition = _hips + bodyRotation * limb.Offset;
				limb.Transform.localRotation = limb.End != null ? AimLeg(limb, toModel, bodyRotation) : Delta(limb, toModel) * limb.Align;
			}
			if (_equip != null)
			{
				// Back to where the game keeps it (so moves don't add up), then into Steve's hand.
				_equip.localPosition = _equipPosition;
				_equip.localRotation = _equipRotation;
				_equip.position = _rightArm.TransformPoint(_hand);
			}
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
			if (_animator != null)
			{
				_animator.cullingMode = _cullingMode;
			}
			if (_equip != null)
			{
				_equip.localPosition = _equipPosition;
				_equip.localRotation = _equipRotation;
			}
			if (_root != null)
			{
				Object.Destroy(_root);
			}
		}

		// The rotation a bone has made since the bind pose, in the model's space.
		private static Quaternion Delta(Part part, Quaternion toModel)
		{
			return toModel * part.Bone.rotation * part.InverseBind;
		}

		// Points the leg down the hip-to-foot line, keeping the thigh's twist so the feet face where the chibi's do.
		private Quaternion AimLeg(Part leg, Quaternion toModel, Quaternion bodyRotation)
		{
			Vector3 line = _model.InverseTransformDirection(leg.End.position - leg.Bone.position);
			Vector3 down = bodyRotation * leg.Straighten * Quaternion.Inverse(bodyRotation) * line;
			Vector3 forward = Vector3.ProjectOnPlane(Delta(leg, toModel) * Vector3.forward, down);
			if (down.sqrMagnitude < 1e-8f || forward.sqrMagnitude < 1e-8f)
			{
				return Delta(leg, toModel) * leg.Align;
			}
			return Quaternion.LookRotation(forward, -down);
		}

		private static SkinnedMeshRenderer FindSkinned(Transform model)
		{
			foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if (renderer.sharedMesh != null && Array.TrueForAll(Rig, r => Array.Exists(renderer.bones, b => b != null && b.name == r.bone)))
				{
					return renderer;
				}
			}
			return null;
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
