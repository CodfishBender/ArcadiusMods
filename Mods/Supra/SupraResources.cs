using System;
using System.Collections.Generic;
using System.IO;
using ArcadiusModLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ArcadiusSupra
{
	// The car's meshes, colours and icon from Model/ (made by convert_model.py), shared by every Supra.
	internal sealed class SupraResources
	{
#pragma warning disable CS0649 // set by Newtonsoft
		// Model/supra.json, written by convert_model.py: metres, wheels on the ground at y = 0, front at +Z.
		private sealed class ModelFile
		{
			public float length;

			public List<PartFile> body;

			public List<WheelFile> wheels;
		}

		private class PartFile
		{
			public float[] colour;

			public float[] vertices;

			public float[] normals;

			public int[] triangles;
		}

		private sealed class WheelFile : PartFile
		{
			public float[] centre;

			public float radius;
		}
#pragma warning restore CS0649

		public sealed class Wheel
		{
			public Mesh Mesh;

			public Vector3 Centre;

			public float Radius;

			public Color Colour;
		}

		private readonly List<Material> _materials = new List<Material>();

		private readonly Dictionary<Color, Material> _byColour = new Dictionary<Color, Material>();

		private Texture2D _iconTexture;

		public Mesh Body { get; private set; }

		public Color[] BodyColours { get; private set; }

		public List<Wheel> Wheels { get; } = new List<Wheel>();

		// Middle of the wheelbase, for tilting the body.
		public Vector3 Pivot { get; private set; }

		public Sprite Icon { get; private set; }

		public static SupraResources Load(string directory, float length, ModLog log)
		{
			SupraResources resources = new SupraResources();
			try
			{
				string modelDirectory = Path.Combine(directory, "Model");
				ModelFile model = Newtonsoft.Json.JsonConvert.DeserializeObject<ModelFile>(File.ReadAllText(Path.Combine(modelDirectory, "supra.json")));
				float scale = Mathf.Max(0.1f, length) / model.length;
				resources.Body = BuildMesh("Supra body", model.body, scale);
				resources.BodyColours = model.body.ConvertAll(p => ToColour(p.colour)).ToArray();
				Vector3 centre = Vector3.zero;
				foreach (WheelFile wheel in model.wheels)
				{
					Wheel part = new Wheel
					{
						Mesh = BuildMesh("Supra wheel", new List<PartFile> { wheel }, scale),
						Centre = new Vector3(wheel.centre[0], wheel.centre[1], wheel.centre[2]) * scale,
						Radius = wheel.radius * scale,
						Colour = ToColour(wheel.colour),
					};
					resources.Wheels.Add(part);
					centre += part.Centre / model.wheels.Count;
				}
				resources.Pivot = centre;
				string iconPath = Path.Combine(modelDirectory, "icon.png");
				if (File.Exists(iconPath))
				{
					Texture2D icon = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Supra icon" };
					if (icon.LoadImage(File.ReadAllBytes(iconPath)))
					{
						resources._iconTexture = icon;
						resources.Icon = Sprite.Create(icon, new Rect(0f, 0f, icon.width, icon.height), new Vector2(0.5f, 0.5f), icon.width);
					}
				}
				log.Info("Loaded the Supra: " + resources.Body.triangles.Length / 3 + " body triangles, " + resources.Wheels.Count + " wheels, " + length + " m long.");
				return resources;
			}
			catch (Exception ex)
			{
				log.Error("Could not load the Supra model; press F9 after fixing it.", ex);
				resources.Destroy();
				return null;
			}
		}

		// One submesh per part.
		private static Mesh BuildMesh(string name, List<PartFile> parts, float scale)
		{
			List<Vector3> vertices = new List<Vector3>();
			List<Vector3> normals = new List<Vector3>();
			List<int[]> submeshes = new List<int[]>();
			foreach (PartFile part in parts)
			{
				int start = vertices.Count;
				for (int i = 0; i < part.vertices.Length; i += 3)
				{
					vertices.Add(new Vector3(part.vertices[i], part.vertices[i + 1], part.vertices[i + 2]) * scale);
					normals.Add(new Vector3(part.normals[i], part.normals[i + 1], part.normals[i + 2]));
				}
				int[] triangles = new int[part.triangles.Length];
				for (int i = 0; i < triangles.Length; i++)
				{
					triangles[i] = part.triangles[i] + start;
				}
				submeshes.Add(triangles);
			}
			Mesh mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
			mesh.SetVertices(vertices);
			mesh.SetNormals(normals);
			mesh.subMeshCount = submeshes.Count;
			for (int i = 0; i < submeshes.Count; i++)
			{
				mesh.SetTriangles(submeshes[i], i);
			}
			mesh.RecalculateBounds();
			return mesh;
		}

		// A copy of the chibi's skin material (the game's URP Lit setup) in a flat colour.
		public Material GetMaterial(Material template, Color colour)
		{
			if (_byColour.TryGetValue(colour, out Material material))
			{
				return material;
			}
			Shader shader = template != null ? template.shader : Shader.Find("Universal Render Pipeline/Lit");
			material = template != null ? new Material(template) : new Material(shader);
			material.name = "Supra " + colour;
			material.mainTexture = Texture2D.whiteTexture;
			foreach (string property in new[] { "_BaseMap", "_MainTex" })
			{
				if (material.HasProperty(property))
				{
					material.SetTexture(property, Texture2D.whiteTexture);
				}
			}
			foreach (string property in new[] { "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" })
			{
				if (material.HasProperty(property))
				{
					material.SetTexture(property, null);
				}
			}
			foreach (string property in new[] { "_BaseColor", "_Color" })
			{
				if (material.HasProperty(property))
				{
					material.SetColor(property, colour);
				}
			}
			_byColour[colour] = material;
			_materials.Add(material);
			return material;
		}

		public void Destroy()
		{
			if (Body != null)
			{
				Object.Destroy(Body);
			}
			foreach (Wheel wheel in Wheels)
			{
				Object.Destroy(wheel.Mesh);
			}
			foreach (Material material in _materials)
			{
				Object.Destroy(material);
			}
			if (Icon != null)
			{
				Object.Destroy(Icon);
				Object.Destroy(_iconTexture);
			}
		}

		// The converter's colours are linear; materials take sRGB colours.
		private static Color ToColour(float[] rgb)
		{
			return new Color(rgb[0], rgb[1], rgb[2]).gamma;
		}
	}
}
