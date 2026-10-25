using System;
using System.Collections.Generic;
using ArcadiusModLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArcadiusMiku
{
	// One chibi turned into Miku: a SkinnedMeshRenderer on Miku's own skeleton (same bone names as the chibi's,
	// Miku's proportions). Each Miku bone copies the rotation its chibi bone has made since the bind pose (a T-pose,
	// like Miku's), so the game's animations, emotes and held tools keep working.
	internal sealed class MikuBody
	{
		private sealed class Bone
		{
			public Transform Transform;

			// The chibi bone it copies; null to turn with its parent.
			public Transform Follow;

			public Quaternion InverseBind;

			// Turns Miku's limb onto the chibi limb's bind direction (both T-poses, so a small correction).
			public Quaternion Align = Quaternion.identity;

			public int Parent;
		}

		// Limbs whose direction differs a little between Miku and the chibi.
		private static readonly string[] Aligned = { "upper.Arm.", "forearm.", "thigh.", "shin." };

		private readonly List<Bone> _bones = new List<Bone>();

		private readonly List<Renderer> _hidden = new List<Renderer>();

		private Quaternion[] _deltas;

		private Transform _model;

		// The chibi's Animator culls by its renderers, which are hidden, so it's kept animating.
		private Animator _animator;

		private AnimatorCullingMode _cullingMode;

		private GameObject _root;

		private Transform _hips;

		private Vector3 _hipsBind;

		private Transform _chibiHips;

		private Vector3 _chibiHipsBind;

		// Miku's hips move this much per metre the chibi's do (leg length ratio).
		private float _hipsScale;

		// Ankles, to keep Miku's feet as high above the ground as the chibi's.
		private Transform[] _feet;

		private Transform[] _chibiFeet;

		private float _footBind;

		private float _chibiFootBind;

		// The game parents held tools here, under the chibi's right hand; it's moved to Miku's right hand.
		private Transform _equip;

		private Vector3 _equipPosition;

		private Quaternion _equipRotation;

		private Transform _rightHand;

		private Vector3 _grip;

		private MikuBody()
		{
		}

		public Chibi_Visual Visual { get; private set; }

		public bool IsAlive => Visual != null && _model != null && _root != null;

		public static MikuBody Create(Chibi_Visual visual, MikuResources resources, ModLog log)
		{
			Transform hips = FindChild(visual.transform, "hips");
			Transform model = hips != null && hips.parent != null ? hips.parent.parent : null;
			Dictionary<string, (Transform bone, Matrix4x4 bind)> binds = model == null ? null : ChibiBinds(model);
			if (binds == null || !binds.ContainsKey("hips") || !binds.ContainsKey("hand.R") || !binds.ContainsKey("foot.L") || !binds.ContainsKey("foot.R"))
			{
				log.Warning(visual.name + " has no chibi skeleton; leaving it as it is.");
				return null;
			}

			MikuBody body = new MikuBody { Visual = visual, _model = model };
			body._root = new GameObject("Miku");
			body._root.transform.SetParent(model, false);
			Renderer head = FindChild(model, "Model_Head")?.GetComponent<Renderer>();
			int layer = head != null ? head.gameObject.layer : visual.gameObject.layer;

			Transform[] transforms = new Transform[resources.Bones.Count];
			for (int i = 0; i < resources.Bones.Count; i++)
			{
				MikuResources.Bone bone = resources.Bones[i];
				GameObject boneObject = new GameObject(bone.Name) { layer = layer };
				Transform parent = bone.Parent < 0 ? body._root.transform : transforms[bone.Parent];
				boneObject.transform.SetParent(parent, false);
				boneObject.transform.localPosition = bone.Parent < 0 ? bone.Head : bone.Head - resources.Bones[bone.Parent].Head;
				transforms[i] = boneObject.transform;
				Bone part = new Bone { Transform = boneObject.transform, Parent = bone.Parent };
				if (binds.TryGetValue(bone.Name, out (Transform bone, Matrix4x4 bind) chibi))
				{
					part.Follow = chibi.bone;
					part.InverseBind = Quaternion.Inverse(Quaternion.LookRotation(chibi.bind.GetColumn(2), chibi.bind.GetColumn(1)));
					if (Array.Exists(Aligned, a => bone.Name.StartsWith(a, StringComparison.Ordinal)))
					{
						part.Align = Quaternion.FromToRotation(bone.End - bone.Head, chibi.bind.GetColumn(1));
					}
				}
				body._bones.Add(part);
			}
			body._deltas = new Quaternion[body._bones.Count];

			SkinnedMeshRenderer renderer = new GameObject("Miku mesh") { layer = layer }.AddComponent<SkinnedMeshRenderer>();
			renderer.transform.SetParent(body._root.transform, false);
			renderer.sharedMesh = resources.Mesh;
			renderer.bones = transforms;
			renderer.rootBone = transforms[0];
			renderer.sharedMaterials = resources.GetMaterials(head != null ? head.sharedMaterial : null);
			renderer.updateWhenOffscreen = true;

			int Index(string name) => resources.Bones.FindIndex(b => b.Name == name);
			body._hips = transforms[0];
			body._hipsBind = resources.Bones[0].Head;
			body._chibiHips = binds["hips"].bone;
			body._chibiHipsBind = binds["hips"].bind.GetColumn(3);
			body._hipsScale = body._hipsBind.y / Mathf.Max(0.01f, body._chibiHipsBind.y);
			body._feet = new[] { transforms[Index("foot.L")], transforms[Index("foot.R")] };
			body._chibiFeet = new[] { binds["foot.L"].bone, binds["foot.R"].bone };
			body._footBind = Mathf.Min(resources.Bones[Index("foot.L")].Head.y, resources.Bones[Index("foot.R")].Head.y);
			body._chibiFootBind = Mathf.Min(binds["foot.L"].bind.GetColumn(3).y, binds["foot.R"].bind.GetColumn(3).y);
			body._rightHand = transforms[Index("hand.R")];
			body._grip = resources.Grip - resources.Bones[Index("hand.R")].Head;

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
			Quaternion modelRotation = _model.rotation;
			Quaternion toModel = Quaternion.Inverse(modelRotation);
			// Parents come first, so a bone that follows its parent can reuse the parent's rotation.
			for (int i = 0; i < _bones.Count; i++)
			{
				Bone bone = _bones[i];
				_deltas[i] = bone.Follow != null ? toModel * bone.Follow.rotation * bone.InverseBind : bone.Parent >= 0 ? _deltas[bone.Parent] : Quaternion.identity;
				bone.Transform.rotation = modelRotation * _deltas[i] * bone.Align;
			}

			// The hips move like the chibi's, scaled to Miku's legs; then the feet are put as high above the ground as
			// the chibi's, since the two pairs of legs bend differently.
			Vector3 chibiHips = _model.InverseTransformPoint(_chibiHips.position);
			_hips.localPosition = _hipsBind + (chibiHips - _chibiHipsBind) * _hipsScale;
			float foot = Mathf.Min(_model.InverseTransformPoint(_feet[0].position).y, _model.InverseTransformPoint(_feet[1].position).y);
			float chibiFoot = Mathf.Min(_model.InverseTransformPoint(_chibiFeet[0].position).y, _model.InverseTransformPoint(_chibiFeet[1].position).y);
			_hips.localPosition += Vector3.up * (_footBind + (chibiFoot - _chibiFootBind) - foot);

			if (_equip != null)
			{
				// Back to where the game keeps it (so moves don't add up), then into Miku's hand.
				_equip.localPosition = _equipPosition;
				_equip.localRotation = _equipRotation;
				_equip.position = _rightHand.TransformPoint(_grip);
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

		// Each chibi bone and its bind pose in the model's space, from the chibi's skinned meshes (clothes).
		private static Dictionary<string, (Transform, Matrix4x4)> ChibiBinds(Transform model)
		{
			Dictionary<string, (Transform, Matrix4x4)> binds = new Dictionary<string, (Transform, Matrix4x4)>();
			foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if (renderer.sharedMesh == null)
				{
					continue;
				}
				Matrix4x4 toModel = model.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
				Matrix4x4[] bindposes = renderer.sharedMesh.bindposes;
				Transform[] bones = renderer.bones;
				for (int i = 0; i < bones.Length && i < bindposes.Length; i++)
				{
					if (bones[i] != null && !binds.ContainsKey(bones[i].name))
					{
						binds[bones[i].name] = (bones[i], toModel * bindposes[i].inverse);
					}
				}
			}
			return binds;
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
