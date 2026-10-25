using System;
using System.Collections.Generic;
using System.IO;
using ArcadiusModLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ArcadiusMiku
{
	// The skinned mesh, textures and icon from Model/ (made by convert_model.py), shared by every Miku.
	internal sealed class MikuResources
	{
#pragma warning disable CS0649 // set by Newtonsoft
		// Model/miku.json, written by convert_model.py: positions in model units, feet at y = 0.
		private sealed class ModelFile
		{
			public List<BoneFile> bones;

			public float[] grip;

			public float[] vertices;

			public float[] normals;

			public float[] uv;

			public int[] boneIndices;

			public float[] boneWeights;

			public List<MaterialFile> materials;
		}

		private sealed class BoneFile
		{
			public string name;

			public string parent;

			public float[] head;

			public float[] end;
		}

		private sealed class MaterialFile
		{
			public string name;

			public string texture;

			public bool cutout;

			public int[] triangles;
		}
#pragma warning restore CS0649

		public sealed class Bone
		{
			public string Name;

			// Index in Bones, or -1 for the hips.
			public int Parent;

			// Joint, and where the bone points (its child's joint or its tail), in the model's space in metres.
			public Vector3 Head;

			public Vector3 End;
		}

		private readonly List<(string texture, bool cutout)> _materialFiles = new List<(string, bool)>();

		private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();

		private Material[] _materials;

		public List<Bone> Bones { get; } = new List<Bone>();

		public Mesh Mesh { get; private set; }

		// Where held tools go, in the model's space.
		public Vector3 Grip { get; private set; }

		public Sprite Icon { get; private set; }

		public static MikuResources Load(string directory, float height, ModLog log)
		{
			MikuResources resources = new MikuResources();
			try
			{
				string modelDirectory = Path.Combine(directory, "Model");
				ModelFile model = Newtonsoft.Json.JsonConvert.DeserializeObject<ModelFile>(File.ReadAllText(Path.Combine(modelDirectory, "miku.json")));
				float top = 0f;
				for (int i = 1; i < model.vertices.Length; i += 3)
				{
					top = Mathf.Max(top, model.vertices[i]);
				}
				float scale = Mathf.Max(0.1f, height) / top;
				foreach (BoneFile bone in model.bones)
				{
					resources.Bones.Add(new Bone
					{
						Name = bone.name,
						Parent = bone.parent == null ? -1 : resources.Bones.FindIndex(b => b.Name == bone.parent),
						Head = ToVector(bone.head, 0) * scale,
						End = ToVector(bone.end, 0) * scale,
					});
				}
				resources.Grip = ToVector(model.grip, 0) * scale;
				resources.Mesh = resources.BuildMesh(model, scale);
				foreach (MaterialFile material in model.materials)
				{
					resources._materialFiles.Add((material.texture, material.cutout));
					if (!resources._textures.ContainsKey(material.texture))
					{
						resources._textures[material.texture] = LoadTexture(Path.Combine(modelDirectory, material.texture), true);
					}
				}
				string iconPath = Path.Combine(modelDirectory, "icon.png");
				if (File.Exists(iconPath))
				{
					Texture2D icon = LoadTexture(iconPath, false);
					resources._textures[iconPath] = icon;
					resources.Icon = Sprite.Create(icon, new Rect(0f, 0f, icon.width, icon.height), new Vector2(0.5f, 0.5f), icon.width);
				}
				log.Info("Loaded Miku: " + model.vertices.Length / 3 + " vertices, " + model.materials.Count + " materials, " + resources.Bones.Count + " bones, " + height + " m tall.");
				return resources;
			}
			catch (Exception ex)
			{
				log.Error("Could not load the Miku model; press F9 after fixing it.", ex);
				resources.Destroy();
				return null;
			}
		}

		private Mesh BuildMesh(ModelFile model, float scale)
		{
			int count = model.vertices.Length / 3;
			Vector3[] vertices = new Vector3[count];
			Vector3[] normals = new Vector3[count];
			Vector2[] uv = new Vector2[count];
			BoneWeight[] weights = new BoneWeight[count];
			for (int i = 0; i < count; i++)
			{
				vertices[i] = ToVector(model.vertices, i * 3) * scale;
				normals[i] = ToVector(model.normals, i * 3);
				uv[i] = new Vector2(model.uv[i * 2], model.uv[i * 2 + 1]);
				int w = i * 4;
				weights[i] = new BoneWeight
				{
					boneIndex0 = model.boneIndices[w], weight0 = model.boneWeights[w],
					boneIndex1 = model.boneIndices[w + 1], weight1 = model.boneWeights[w + 1],
					boneIndex2 = model.boneIndices[w + 2], weight2 = model.boneWeights[w + 2],
					boneIndex3 = model.boneIndices[w + 3], weight3 = model.boneWeights[w + 3],
				};
			}
			// Bones are built with no rotation, so each bind pose is just the joint's position.
			Matrix4x4[] bindposes = new Matrix4x4[Bones.Count];
			for (int i = 0; i < Bones.Count; i++)
			{
				bindposes[i] = Matrix4x4.Translate(-Bones[i].Head);
			}
			Mesh mesh = new Mesh
			{
				name = "Miku",
				indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
				vertices = vertices,
				normals = normals,
				uv = uv,
				boneWeights = weights,
				bindposes = bindposes,
				subMeshCount = model.materials.Count,
			};
			for (int i = 0; i < model.materials.Count; i++)
			{
				mesh.SetTriangles(model.materials[i].triangles, i);
			}
			mesh.RecalculateBounds();
			return mesh;
		}

		// Copies of the chibi's skin material (the game's URP Lit setup), one per submesh, showing Miku's textures.
		public Material[] GetMaterials(Material template)
		{
			if (_materials != null)
			{
				return _materials;
			}
			Shader shader = template != null ? template.shader : Shader.Find("Universal Render Pipeline/Lit");
			_materials = new Material[_materialFiles.Count];
			for (int i = 0; i < _materials.Length; i++)
			{
				(string texture, bool cutout) = _materialFiles[i];
				Material material = template != null ? new Material(template) : new Material(shader);
				material.name = "Miku " + texture;
				material.mainTexture = _textures[texture];
				foreach (string property in new[] { "_BaseMap", "_MainTex" })
				{
					if (material.HasProperty(property))
					{
						material.SetTexture(property, _textures[texture]);
					}
				}
				foreach (string property in new[] { "_BaseColor", "_Color" })
				{
					if (material.HasProperty(property))
					{
						material.SetColor(property, Color.white);
					}
				}
				// The chibi's own maps don't fit Miku's UVs.
				foreach (string property in new[] { "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" })
				{
					if (material.HasProperty(property))
					{
						material.SetTexture(property, null);
					}
				}
				if (cutout)
				{
					material.SetFloat("_AlphaClip", 1f);
					material.SetFloat("_Cutoff", 0.5f);
					material.EnableKeyword("_ALPHATEST_ON");
					material.renderQueue = (int)RenderQueue.AlphaTest;
				}
				_materials[i] = material;
			}
			return _materials;
		}

		public void Destroy()
		{
			if (Mesh != null)
			{
				Object.Destroy(Mesh);
			}
			foreach (Texture2D texture in _textures.Values)
			{
				Object.Destroy(texture);
			}
			if (Icon != null)
			{
				Object.Destroy(Icon);
			}
			if (_materials != null)
			{
				foreach (Material material in _materials)
				{
					Object.Destroy(material);
				}
			}
		}

		private static Texture2D LoadTexture(string path, bool mipmaps)
		{
			Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipmaps) { name = Path.GetFileName(path) };
			if (!texture.LoadImage(File.ReadAllBytes(path)))
			{
				throw new InvalidDataException(path + " is not a PNG or JPG image.");
			}
			texture.wrapMode = TextureWrapMode.Repeat;
			return texture;
		}

		private static Vector3 ToVector(float[] values, int offset)
		{
			return new Vector3(values[offset], values[offset + 1], values[offset + 2]);
		}
	}
}
