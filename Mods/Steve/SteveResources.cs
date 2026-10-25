using System;
using System.Collections.Generic;
using System.IO;
using ArcadiusModLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArcadiusSteve
{
	// Meshes, skin texture and material shared by every Steve.
	internal sealed class SteveResources
	{
#pragma warning disable CS0649 // set by Newtonsoft
		// Model/steve.json, written by convert_model.py: per part, positions in skin pixels (feet at y = 0).
		private sealed class ModelFile
		{
			public Dictionary<string, PartFile> parts;
		}

		private sealed class PartFile
		{
			public float[] min;

			public float[] max;

			public float[] vertices;

			public float[] uv;

			public int[] triangles;
		}
#pragma warning restore CS0649

		public sealed class Part
		{
			public Mesh Mesh;

			// Joint the part rotates around, in metres in the model's space.
			public Vector3 Pivot;

			public Vector3 Min;
		}

		private Material _material;

		private SteveResources()
		{
		}

		public Dictionary<string, Part> Parts { get; } = new Dictionary<string, Part>();

		public Texture2D Skin { get; private set; }

		// Size of one skin pixel in metres.
		public float Pixel { get; private set; }

		public static SteveResources Load(string directory, string skinPath, float height, ModLog log)
		{
			SteveResources resources = new SteveResources();
			try
			{
				string modelPath = Path.Combine(directory, "Model", "steve.json");
				ModelFile model = Newtonsoft.Json.JsonConvert.DeserializeObject<ModelFile>(File.ReadAllText(modelPath));
				float top = 0f;
				foreach (PartFile part in model.parts.Values)
				{
					top = Mathf.Max(top, part.max[1]);
				}
				resources.Pixel = Mathf.Max(0.1f, height) / top;
				foreach (KeyValuePair<string, PartFile> part in model.parts)
				{
					resources.Parts[part.Key] = resources.BuildPart(part.Key, part.Value);
				}

				string fullSkinPath = Path.Combine(directory, skinPath);
				Texture2D skin = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Steve skin" };
				if (!skin.LoadImage(File.ReadAllBytes(fullSkinPath)))
				{
					throw new InvalidDataException(fullSkinPath + " is not a PNG or JPG image.");
				}
				skin.filterMode = FilterMode.Point;
				skin.wrapMode = TextureWrapMode.Clamp;
				resources.Skin = skin;
				log.Info("Loaded Steve: " + resources.Parts.Count + " parts, skin " + skin.width + "x" + skin.height + ", " + height + " m tall.");
				return resources;
			}
			catch (Exception ex)
			{
				log.Error("Could not load the Steve model or skin; press F9 after fixing it.", ex);
				resources.Destroy();
				return null;
			}
		}

		private Part BuildPart(string name, PartFile file)
		{
			Vector3 min = new Vector3(file.min[0], file.min[1], file.min[2]);
			Vector3 max = new Vector3(file.max[0], file.max[1], file.max[2]);
			Vector3 center = (min + max) / 2f;
			// Joints in skin pixels, as in Minecraft: neck, hips, shoulders 2 pixels below the top, hip joints.
			Vector3 pivot;
			if (name == "head" || name == "body")
			{
				pivot = new Vector3(center.x, min.y, center.z);
			}
			else if (name.StartsWith("arm.", StringComparison.Ordinal))
			{
				pivot = new Vector3(center.x, max.y - 2f, center.z);
			}
			else
			{
				pivot = new Vector3(center.x, max.y, center.z);
			}

			Vector3[] vertices = new Vector3[file.vertices.Length / 3];
			Vector2[] uv = new Vector2[vertices.Length];
			for (int i = 0; i < vertices.Length; i++)
			{
				vertices[i] = (new Vector3(file.vertices[i * 3], file.vertices[i * 3 + 1], file.vertices[i * 3 + 2]) - pivot) * Pixel;
				uv[i] = new Vector2(file.uv[i * 2], file.uv[i * 2 + 1]);
			}
			Mesh mesh = new Mesh { name = "Steve " + name, vertices = vertices, uv = uv, triangles = file.triangles };
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return new Part { Mesh = mesh, Pivot = pivot * Pixel, Min = min * Pixel };
		}

		// A copy of the chibi's skin material (the game's URP Lit setup) showing the Steve skin.
		public Material GetMaterial(Material template)
		{
			if (_material == null)
			{
				Shader shader = template != null ? template.shader : Shader.Find("Universal Render Pipeline/Lit");
				_material = template != null ? new Material(template) : new Material(shader);
				_material.name = "Steve";
				_material.mainTexture = Skin;
				foreach (string property in new[] { "_BaseMap", "_MainTex" })
				{
					if (_material.HasProperty(property))
					{
						_material.SetTexture(property, Skin);
					}
				}
				foreach (string property in new[] { "_BaseColor", "_Color" })
				{
					if (_material.HasProperty(property))
					{
						_material.SetColor(property, Color.white);
					}
				}
			}
			return _material;
		}

		public void Destroy()
		{
			foreach (Part part in Parts.Values)
			{
				Object.Destroy(part.Mesh);
			}
			Parts.Clear();
			if (Skin != null)
			{
				Object.Destroy(Skin);
			}
			if (_material != null)
			{
				Object.Destroy(_material);
			}
		}
	}
}
