# Converts the Steve FBX into Model/steve.json, which the mod loads at runtime.
# Usage: blender -b --python convert_model.py -- <steve.fbx> <steve.json>
#
# The FBX is one mesh of six boxes. Each box becomes a part (head, body, arm.L/R, leg.L/R), in Unity
# coordinates (Y up, facing +Z, character's right at +X) and in skin pixels, with the feet at y = 0.
import json
import sys

import bpy
from mathutils import Vector

source, target = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=source)
obj = next(o for o in bpy.data.objects if o.type == "MESH")
mesh = obj.data
uvs = mesh.uv_layers.active.data
world = obj.matrix_world

# Group the faces into boxes (connected vertices).
parent = list(range(len(mesh.vertices)))


def find(i):
    while parent[i] != i:
        parent[i] = parent[parent[i]]
        i = parent[i]
    return i


for poly in mesh.polygons:
    for v in poly.vertices[1:]:
        parent[find(v)] = find(poly.vertices[0])
boxes = {}
for poly in mesh.polygons:
    boxes.setdefault(find(poly.vertices[0]), []).append(poly)
if len(boxes) != 6:
    sys.exit(f"Expected 6 boxes, found {len(boxes)}")

# The face texture (skin pixels 8..16, 8..16) is on the head's front: its normal is the facing direction.
def uv_center(poly):
    return sum((Vector(uvs[i].uv) for i in poly.loop_indices), Vector((0, 0))) / poly.loop_total


front_face = min(mesh.polygons, key=lambda p: (uv_center(p) - Vector((12 / 64, 1 - 12 / 64))).length)
forward = (world.to_3x3() @ front_face.normal).normalized()
up = Vector((0, 0, 1))
forward = (forward - up * forward.dot(up)).normalized()
right = forward.cross(up)


def to_unity(co):
    p = world @ co
    return Vector((p.dot(right), p.dot(up), p.dot(forward)))


points = [to_unity(v.co) for v in mesh.vertices]
floor = min(p.y for p in points)
height = max(p.y for p in points) - floor
pixel = height / 32  # Steve is 32 skin pixels tall
points = [Vector((p.x / pixel, (p.y - floor) / pixel, p.z / pixel)) for p in points]


def bounds(polys):
    ps = [points[v] for poly in polys for v in poly.vertices]
    return Vector([min(p[i] for p in ps) for i in range(3)]), Vector([max(p[i] for p in ps) for i in range(3)])


def name_of(polys):
    low, high = bounds(polys)
    center = (low + high) / 2
    if low.y > 23:
        return "head"
    if low.y > 11 and abs(center.x) < 1:
        return "body"
    side = "R" if center.x > 0 else "L"
    return ("arm." if low.y > 11 else "leg.") + side


parts = {}
for polys in boxes.values():
    vertices, uv, triangles = [], [], []
    for poly in polys:
        start = len(vertices) // 3
        for loop in poly.loop_indices:
            p = points[mesh.loops[loop].vertex_index]
            vertices += [round(p.x, 4), round(p.y, 4), round(p.z, 4)]
            uv += [round(uvs[loop].uv[0], 6), round(uvs[loop].uv[1], 6)]
        # Unity is left-handed, so the winding is reversed.
        for i in range(1, poly.loop_total - 1):
            triangles += [start, start + i + 1, start + i]
    low, high = bounds(polys)
    parts[name_of(polys)] = {"min": [round(c, 4) for c in low], "max": [round(c, 4) for c in high],
                             "vertices": vertices, "uv": uv, "triangles": triangles}
if sorted(parts) != sorted(["head", "body", "arm.L", "arm.R", "leg.L", "leg.R"]):
    sys.exit("Could not identify the parts: " + ", ".join(parts))

with open(target, "w") as f:
    json.dump({"source": "minecraft-steve/source/steve.fbx", "units": "skin pixels", "parts": parts}, f, indent=1)
for name, part in sorted(parts.items()):
    print(name, "min", part["min"], "max", part["max"])
