# Converts a rigged MMD model (FBX exported by mmd_tools, plus its PMX for the material textures) into
# Model/miku.json, copies its textures next to it and renders the wardrobe icon (Model/icon.png), for
# MikuResources.cs.
# Usage: blender -b --python convert_model.py -- <model.fbx> <model.pmx> <out dir>
#
# Weights are folded onto the game's chibi bones: each MMD bone counts for its nearest ancestor in BONES
# (eyes and accessories -> head, skirt -> hips, twist bones -> arm, fingers -> hand, toes -> foot). Hair bones
# (EX_..._hair_NN) are kept for MikuBody's hair physics, with collision spheres for the head, chest and hips.
# Output is in Unity's axes (Y up, +Z forward, the character's left at -X) in the model's own units, with the
# feet at y = 0 and the triangle winding reversed for the mirrored axes.
import json
import os
import re
import shutil
import struct
import sys

import bpy

# Chibi bone -> MMD bone, parents before children (MikuBody builds the hierarchy in this order).
BONES = [
    ("hips", "下半身", None),
    ("spine", "上半身", "hips"),
    ("chest", "上半身1", "spine"),
    ("upper.chest", "上半身2", "chest"),
    ("neck", "首", "upper.chest"),
    ("head", "頭", "neck"),
]
for side in ("L", "R"):
    BONES += [
        ("shoulder." + side, "肩." + side, "upper.chest"),
        ("upper.Arm." + side, "腕." + side, "shoulder." + side),
        ("forearm." + side, "ひじ." + side, "upper.Arm." + side),
        ("hand." + side, "手首." + side, "forearm." + side),
        ("thigh." + side, "足." + side, "hips"),
        ("shin." + side, "ひざ." + side, "thigh." + side),
        ("foot." + side, "足首." + side, "shin." + side),
    ]
# Bones that point at another bone's head rather than their own tail (MikuBody aims limbs along this line).
ENDS = {"upper.Arm.": "ひじ.", "forearm.": "手首.", "thigh.": "ひざ.", "shin.": "足首."}
HAIR = re.compile(r"^EX_.*_hair_\d+$")
# Bones that hair collides with; each sphere fits the vertices that bone moves most (not the hair itself).
COLLIDERS = ["head", "upper.chest", "chest", "hips"]
# Added to each collider's radius, for the thickness of the hair (model units).
HAIR_MARGIN = 0.25


def to_unity(v):
    return (-v[0], v[2], -v[1])


def pmx_material_textures(path):
    # Material name -> diffuse texture file name, read from the PMX header, vertices, faces and texture table.
    d = open(path, "rb").read()
    o = 0

    def u(fmt):
        nonlocal o
        v = struct.unpack_from("<" + fmt, d, o)
        o += struct.calcsize("<" + fmt)
        return v

    if d[:4] != b"PMX ":
        raise SystemExit(path + " is not a PMX file")
    o = 4
    u("f")
    n, = u("B")
    enc, addvec, vsz, tsz, _, bsz, _, _ = u("%dB" % n)[:8]

    def text():
        nonlocal o
        ln, = u("i")
        s = d[o:o + ln]
        o += ln
        return s.decode("utf-16-le" if enc == 0 else "utf-8")

    for _ in range(4):
        text()
    nv, = u("i")
    for _ in range(nv):
        o += 32 + 16 * addvec
        w, = u("B")
        o += {0: bsz, 1: 2 * bsz + 4, 2: 4 * bsz + 16, 3: 2 * bsz + 40, 4: 4 * bsz + 16}[w] + 4
    nf, = u("i")
    o += nf * vsz
    nt, = u("i")
    textures = [text() for _ in range(nt)]
    result = {}
    nm, = u("i")
    for _ in range(nm):
        name = text()
        text()
        o += 16 + 12 + 4 + 12 + 1 + 16 + 4
        ti, = u({1: "b", 2: "h", 4: "i"}[tsz])
        u({1: "b", 2: "h", 4: "i"}[tsz])
        u("B")
        shared, = u("B")
        o += 1 if shared else tsz
        text()
        u("i")
        result[name] = textures[ti].replace("\\", "/") if ti >= 0 else None
    return result


def has_cutout(image_path):
    image = bpy.data.images.load(image_path)
    alpha = list(image.pixels)[3::4]
    bpy.data.images.remove(image)
    return sum(1 for a in alpha if a < 0.5) > len(alpha) // 100


def render_icon(obj, textures, texture_dir, path):
    # A 128x128 picture of the face for the wardrobe: unlit textures, orthographic, transparent background.
    for material in obj.data.materials:
        material.use_nodes = True
        nodes, links = material.node_tree.nodes, material.node_tree.links
        nodes.clear()
        image = nodes.new("ShaderNodeTexImage")
        image.image = bpy.data.images.load(os.path.join(texture_dir, textures[material.name]))
        emission = nodes.new("ShaderNodeEmission")
        transparent = nodes.new("ShaderNodeBsdfTransparent")
        mix = nodes.new("ShaderNodeMixShader")
        # Back faces are see-through, as the game culls them.
        geometry = nodes.new("ShaderNodeNewGeometry")
        cull = nodes.new("ShaderNodeMixShader")
        output = nodes.new("ShaderNodeOutputMaterial")
        links.new(image.outputs["Color"], emission.inputs["Color"])
        links.new(image.outputs["Alpha"], mix.inputs["Fac"])
        links.new(transparent.outputs[0], mix.inputs[1])
        links.new(emission.outputs[0], mix.inputs[2])
        links.new(geometry.outputs["Backfacing"], cull.inputs["Fac"])
        links.new(mix.outputs[0], cull.inputs[1])
        links.new(transparent.outputs[0], cull.inputs[2])
        links.new(cull.outputs[0], output.inputs["Surface"])
    head = obj.parent.data.bones["頭"]
    top = max((obj.matrix_world @ v.co).z for v in obj.data.vertices if v.co.z > head.head_local.z and abs(v.co.x) < 3)
    centre_z = (head.head_local.z + top) / 2
    scene = bpy.context.scene
    camera = bpy.data.objects.new("IconCamera", bpy.data.cameras.new("IconCamera"))
    scene.collection.objects.link(camera)
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = (top - head.head_local.z) * 1.5
    # MMD models face -Y in Blender.
    camera.location = (0.0, -50.0, centre_z)
    camera.rotation_euler = (1.5707963, 0.0, 0.0)
    scene.camera = camera
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 16
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = 128
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    fbx, pmx, out = sys.argv[sys.argv.index("--") + 1:][:3]
    os.makedirs(out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    meshes = [o for o in bpy.data.objects if o.type == "MESH" and o.vertex_groups and o.parent and o.parent.type == "ARMATURE"]
    if not meshes:
        raise SystemExit("No skinned mesh in " + fbx)
    obj = max(meshes, key=lambda o: len(o.data.vertices))
    mesh = obj.data
    bones = obj.parent.data.bones
    world = obj.matrix_world
    missing = [mmd for _, mmd, _ in BONES if mmd not in bones]
    if missing:
        raise SystemExit("Missing MMD bones: " + ", ".join(missing))

    # Hair bones, parents first, each under its nearest kept ancestor.
    chibi_by_mmd = {mmd: name for name, mmd, _ in BONES}
    depth = lambda b: 0 if b.parent is None else 1 + depth(b.parent)
    for b in sorted((b for b in bones if HAIR.match(b.name)), key=depth):
        parent = b.parent
        while parent is not None and parent.name not in chibi_by_mmd:
            parent = parent.parent
        if parent is None:
            continue
        BONES.append((b.name, b.name, chibi_by_mmd[parent.name]))
        chibi_by_mmd[b.name] = b.name
    physics = {name for name, mmd, _ in BONES if HAIR.match(mmd)}

    index = {name: i for i, (name, _, _) in enumerate(BONES)}
    by_mmd = {mmd: index[name] for name, mmd, _ in BONES}

    def target(bone_name):
        b = bones.get(bone_name)
        while b is not None:
            if b.name in by_mmd:
                return by_mmd[b.name]
            b = b.parent
        return index["hips"]

    group_target = [target(g.name) for g in obj.vertex_groups]

    floor = min((world @ v.co).z for v in mesh.vertices)

    def point(co):
        x, y, z = to_unity(world @ co)
        return (x, y - floor, z)

    # Per Blender vertex: the four strongest chibi bones.
    weights = []
    for v in mesh.vertices:
        acc = {}
        for g in v.groups:
            if g.weight > 0:
                t = group_target[g.group]
                acc[t] = acc.get(t, 0.0) + g.weight
        if not acc:
            acc = {index["hips"]: 1.0}
        top = sorted(acc.items(), key=lambda kv: -kv[1])[:4]
        total = sum(w for _, w in top)
        top = [(b, w / total) for b, w in top] + [(0, 0.0)] * (4 - len(top))
        weights.append(top)

    uv_layer = next((l for l in mesh.uv_layers if l.active_render), mesh.uv_layers[0]).data
    normals = mesh.corner_normals
    textures = pmx_material_textures(pmx)
    vertices, uvs, norms, bone_index, bone_weight = [], [], [], [], []
    remap = {}
    materials = []
    for mi, material in enumerate(mesh.materials):
        texture = textures.get(material.name)
        if texture is None:
            raise SystemExit("No PMX texture for material " + material.name)
        triangles = []
        for poly in mesh.polygons:
            if poly.material_index != mi:
                continue
            corners = []
            for li in poly.loop_indices:
                vi = mesh.loops[li].vertex_index
                uv = tuple(round(c, 5) for c in uv_layer[li].uv)
                n = tuple(round(c, 3) for c in to_unity(normals[li].vector))
                key = (vi, uv, n)
                if key not in remap:
                    remap[key] = len(vertices)
                    vertices.append(point(mesh.vertices[vi].co))
                    uvs.append(uv)
                    norms.append(n)
                    bone_index.append([b for b, _ in weights[vi]])
                    bone_weight.append([round(w, 4) for _, w in weights[vi]])
                corners.append(remap[key])
            triangles += [corners[0], corners[2], corners[1]]
        source = os.path.join(os.path.dirname(pmx), texture)
        shutil.copyfile(source, os.path.join(out, os.path.basename(texture)))
        materials.append({"name": material.name, "texture": os.path.basename(texture), "cutout": has_cutout(source), "triangles": triangles})

    def head(mmd):
        return point(bones[mmd].head_local)

    bone_list = []
    for name, mmd, parent in BONES:
        stem = next((k for k in ENDS if name.startswith(k)), None)
        end = head(ENDS[stem] + name.rsplit(".", 1)[1]) if stem else point(bones[mmd].tail_local)
        bone_list.append({"name": name, "parent": parent, "head": head(mmd), "end": end, "physics": name in physics})
    # Where held tools go: between the right wrist and the middle finger's knuckle.
    wrist, knuckle = head("手首.R"), head("中指１.R")
    grip = tuple(w + (k - w) * 0.6 for w, k in zip(wrist, knuckle))

    colliders = []
    for name in COLLIDERS:
        b = index[name]
        points = [point(v.co) for v, w in zip(mesh.vertices, weights) if w[0][0] == b and not any(BONES[i][0] in physics for i, x in w if x > 0)]
        if not points:
            continue
        centre = tuple(sum(p[k] for p in points) / len(points) for k in range(3))
        flat_distances = sorted(((p[0] - centre[0]) ** 2 + (p[2] - centre[2]) ** 2) ** 0.5 for p in points)
        radius = flat_distances[int(len(flat_distances) * 0.7)] + HAIR_MARGIN
        colliders.append({"bone": name, "centre": [round(c, 4) for c in centre], "radius": round(radius, 4)})

    flat = lambda rows, digits: [round(c, digits) for row in rows for c in row]
    model = {
        "bones": [{**b, "head": [round(c, 4) for c in b["head"]], "end": [round(c, 4) for c in b["end"]]} for b in bone_list],
        "grip": [round(c, 4) for c in grip],
        "colliders": colliders,
        "vertices": flat(vertices, 4),
        "normals": flat(norms, 3),
        "uv": flat(uvs, 5),
        "boneIndices": [i for row in bone_index for i in row],
        "boneWeights": flat(bone_weight, 4),
        "materials": materials,
    }
    with open(os.path.join(out, "miku.json"), "w") as f:
        json.dump(model, f, separators=(",", ":"))

    render_icon(obj, textures, os.path.dirname(pmx), os.path.join(out, "icon.png"))

    height = max(v[1] for v in vertices)
    print("Wrote %d vertices, %d triangles, %d materials, height %.3f units" % (len(vertices), sum(len(m["triangles"]) for m in materials) // 3, len(materials), height))
    for b in bone_list:
        print("  %-20s head %-24s parent %s%s" % (b["name"], tuple(round(c, 2) for c in b["head"]), b["parent"], "  (physics)" if b["physics"] else ""))
    for c in colliders:
        print("  collider %-12s centre %s radius %.2f" % (c["bone"], tuple(round(x, 2) for x in c["centre"]), c["radius"]))
    for m in materials:
        print("  material %-8s %-24s cutout=%s triangles=%d" % (m["name"], m["texture"], m["cutout"], len(m["triangles"]) // 3))
    used = {}
    for row, w in zip(bone_index, bone_weight):
        for b, x in zip(row, w):
            used[BONES[b][0]] = used.get(BONES[b][0], 0) + x
    print("  weight per bone:", {k: round(v, 1) for k, v in sorted(used.items(), key=lambda kv: -kv[1])})


main()
