# Converts the Toyota Supra .blend into Model/supra.json (flat-coloured body and four wheels) and renders the
# wardrobe icon (Model/icon.png), for SupraResources.cs. It's a gag: hidden parts (brakes, suspension, engine bay,
# rivets, screws) are dropped, the wheels are simplified, and every material becomes a flat colour. The free
# model has no bonnet (the engine bay is open), so one is made to fit the gap between the wings.
# Usage: blender -b <supra.blend> --python convert_model.py -- <out dir>
#
# Output is in Unity's axes (Y up, +Z forward, left-handed) in metres, with the wheels' bottoms at y = 0 and the
# triangle winding reversed for the mirrored axes. Each wheel's vertices are relative to its centre.
import json
import math
import os
import sys

import bmesh
import bpy

# Material -> flat colour (linear RGB). Materials missing here use DEFAULT.
COLOURS = {
    "Livery": (1.0, 0.32, 0.02),
    "Front_Bumper": (1.0, 0.32, 0.02),
    "Glass": (0.03, 0.04, 0.05),
    "Window frames": (0.02, 0.02, 0.02),
    "Wipers": (0.02, 0.02, 0.02),
    "bottom": (0.03, 0.03, 0.03),
    "exhaust": (0.25, 0.25, 0.27),
    "interior black": (0.03, 0.03, 0.03),
    "interior1": (0.05, 0.05, 0.05),
    "headlights": (0.85, 0.85, 0.8),
    "Tail Lights": (0.7, 0.02, 0.02),
    "Spoiler": (0.04, 0.04, 0.04),
    "front spoiler": (0.04, 0.04, 0.04),
    "Mirror": (0.5, 0.5, 0.55),
    "Licence Plate": (0.9, 0.9, 0.9),
    "wheel": (0.06, 0.06, 0.06),
}
DEFAULT = (0.3, 0.3, 0.3)
# Parts nobody sees in game.
SKIP_MATERIALS = {"Shock Absorber", "Brake Caliper", "Brake Disc", "rivet", "screw", "gauges", "engine bay", "eng", "radiator"}
WHEEL_MATERIAL = "wheel"
WHEEL_DECIMATE = 0.2
# Bonnet: grid size, half width (metres, it tucks under the wings), how far below the wing tops it sits, and
# how much higher its middle is than its edges.
BONNET_GRID = (12, 18)
BONNET_HALF_WIDTH = 1.0
BONNET_DROP = 0.01
BONNET_CROWN = 0.04


def evaluated_mesh(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = obj.evaluated_get(depsgraph).to_mesh().copy()
    mesh.transform(obj.matrix_world)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    return mesh


def decimate(mesh, ratio):
    obj = bpy.data.objects.new("decimate", mesh)
    bpy.context.scene.collection.objects.link(obj)
    modifier = obj.modifiers.new("decimate", "DECIMATE")
    modifier.ratio = ratio
    depsgraph = bpy.context.evaluated_depsgraph_get()
    result = obj.evaluated_get(depsgraph).to_mesh().copy()
    bpy.data.objects.remove(obj)
    bm = bmesh.new()
    bm.from_mesh(result)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(result)
    bm.free()
    return result


def main():
    out = sys.argv[sys.argv.index("--") + 1]
    os.makedirs(out, exist_ok=True)
    objects = [o for o in bpy.context.scene.objects if o.type == "MESH" and o.visible_get() and o.material_slots]
    wheels = [o for o in objects if any(s.material and s.material.name == WHEEL_MATERIAL for s in o.material_slots)]
    if len(wheels) != 4:
        raise SystemExit("Expected 4 wheels (material %r), found %d" % (WHEEL_MATERIAL, len(wheels)))

    # Car axes: length along whichever of X/Y the wheels spread out most; the front is where the bumper is.
    centres = [sum((o.matrix_world @ v.co for v in o.data.vertices), start=o.matrix_world.translation * 0) / len(o.data.vertices) for o in wheels]
    spread_x = max(c.x for c in centres) - min(c.x for c in centres)
    spread_y = max(c.y for c in centres) - min(c.y for c in centres)
    if spread_x > spread_y:
        raise SystemExit("Expected the car's length along Blender's Y axis")
    bumper = bpy.context.scene.objects.get("Front_Bumper")
    front_positive = bumper is not None and (bumper.matrix_world @ bumper.data.vertices[0].co).y > 0
    # Unity: Y up, +Z forward. Mirrors one axis either way, so the winding is reversed below.
    to_unity = (lambda v: (v[0], v[2], v[1])) if front_positive else (lambda v: (-v[0], v[2], -v[1]))
    # The inverse, for putting the made-up bonnet into the icon render.
    from_unity = (lambda v: (v[0], v[2], v[1])) if front_positive else (lambda v: (-v[0], -v[2], v[1]))

    groups = {}
    wheel_parts = []
    floor = None
    skipped = 0
    for obj in objects:
        mesh = evaluated_mesh(obj)
        is_wheel = obj in wheels
        if is_wheel:
            mesh = decimate(mesh, WHEEL_DECIMATE)
        names = [s.material.name if s.material else None for s in obj.material_slots]
        corner_normals = mesh.corner_normals
        if is_wheel:
            points = [to_unity(v.co) for v in mesh.vertices]
            centre = tuple((max(p[k] for p in points) + min(p[k] for p in points)) / 2 for k in range(3))
            radius = (max(p[1] for p in points) - min(p[1] for p in points)) / 2
            part = {"centre": centre, "radius": radius, "vertices": [], "normals": [], "triangles": []}
            wheel_parts.append(part)
        for poly in mesh.polygons:
            name = names[poly.material_index] if poly.material_index < len(names) else None
            if name in SKIP_MATERIALS:
                skipped += 1
                continue
            if is_wheel:
                target = part
                offset = part["centre"]
            else:
                target = groups.setdefault(COLOURS.get(name, DEFAULT), {"vertices": [], "normals": [], "triangles": []})
                offset = (0.0, 0.0, 0.0)
            corners = []
            for li in poly.loop_indices:
                p = to_unity(mesh.vertices[mesh.loops[li].vertex_index].co)
                floor = p[1] if floor is None else min(floor, p[1])
                target["vertices"].append(tuple(p[k] - offset[k] for k in range(3)))
                target["normals"].append(to_unity(corner_normals[li].vector))
                corners.append(len(target["vertices"]) - 1)
            target["triangles"] += [corners[0], corners[2], corners[1]]

    # Feet of the wheels on the ground.
    for group in groups.values():
        group["vertices"] = [(x, y - floor, z) for x, y, z in group["vertices"]]
    for part in wheel_parts:
        x, y, z = part["centre"]
        part["centre"] = (x, y - floor, z)

    bonnet_vertices, bonnet_faces = add_bonnet(groups)

    flat = lambda rows, digits: [round(c, digits) for row in rows for c in row]
    body = [{"colour": list(colour), "vertices": flat(g["vertices"], 4), "normals": flat(g["normals"], 3), "triangles": g["triangles"]} for colour, g in groups.items()]
    wheels_out = [{"centre": [round(c, 4) for c in w["centre"]], "radius": round(w["radius"], 4), "colour": list(COLOURS[WHEEL_MATERIAL]),
                   "vertices": flat(w["vertices"], 4), "normals": flat(w["normals"], 3), "triangles": w["triangles"]} for w in wheel_parts]
    all_z = [v[2] for g in groups.values() for v in g["vertices"]]
    length = max(all_z) - min(all_z)
    with open(os.path.join(out, "supra.json"), "w") as f:
        json.dump({"length": round(length, 4), "body": body, "wheels": wheels_out}, f, separators=(",", ":"))

    bonnet_mesh = bpy.data.meshes.new("Bonnet")
    bonnet_mesh.from_pydata([from_unity((x, y + floor, z)) for x, y, z in bonnet_vertices], [], bonnet_faces)
    bonnet = bpy.data.objects.new("Bonnet", bonnet_mesh)
    bonnet.data.materials.append(bpy.data.materials["Livery"])
    bpy.context.scene.collection.objects.link(bonnet)
    render_icon(objects + [bonnet], os.path.join(out, "icon.png"))

    print("Wrote body: %d colours, %d triangles; wheels: %s; length %.2f m; front at Blender %sY; skipped %d hidden-part triangles" % (
        len(body), sum(len(g["triangles"]) for g in body) // 3, [len(w["triangles"]) // 3 for w in wheels_out], length,
        "+" if front_positive else "-", skipped))
    for w in wheels_out:
        print("  wheel centre %s radius %.3f" % (tuple(w["centre"]), w["radius"]))


def add_bonnet(groups):
    # A curved panel from under the windscreen's base to just behind the headlights' front. At each point along the car
    # it follows the tops of the wings (the paint's highest points near the sides), crowned in the middle.
    paint = groups[COLOURS["Livery"]]
    # Starts a little under the windscreen so no gap shows at the base.
    rear = max(v[2] for v in groups[COLOURS["Glass"]]["vertices"]) - 0.12
    front = max(v[2] for v in groups[COLOURS["headlights"]]["vertices"]) - 0.15
    wings = [v for v in paint["vertices"] if 0.8 < abs(v[0]) < 1.1 and rear - 0.1 < v[2] < front + 0.2]
    slices = 24
    tops = []
    for i in range(slices + 1):
        z = rear + (front - rear) * i / slices
        near = [v[1] for v in wings if abs(v[2] - z) < (front - rear) / slices]
        tops.append(max(near) if near else None)
    known = [t for t in tops if t is not None]
    if not known:
        raise SystemExit("Couldn't find the wings to fit a bonnet to")
    for i, t in enumerate(tops):
        if t is None:
            tops[i] = known[0] if i == 0 else tops[i - 1]
    # A bonnet only slopes down towards the front: a slice that only caught low points (door gap) takes the height
    # of the slice in front of it.
    for i in range(slices - 1, -1, -1):
        tops[i] = max(tops[i], tops[i + 1])

    def height(x, z):
        f = (z - rear) / (front - rear) * slices
        i = min(slices - 1, max(0, int(f)))
        top = tops[i] + (tops[i + 1] - tops[i]) * (f - i)
        return top - BONNET_DROP + BONNET_CROWN * (1 - (x / BONNET_HALF_WIDTH) ** 2)

    nx, nz = BONNET_GRID
    grid = [[None] * (nx + 1) for _ in range(nz + 1)]
    for j in range(nz + 1):
        for i in range(nx + 1):
            x = -BONNET_HALF_WIDTH + 2 * BONNET_HALF_WIDTH * i / nx
            z = rear + (front - rear) * j / nz
            grid[j][i] = (x, height(x, z), z)

    def normal(i, j):
        a = grid[j][min(i + 1, nx)]; b = grid[j][max(i - 1, 0)]
        c = grid[min(j + 1, nz)][i]; d = grid[max(j - 1, 0)][i]
        tx = [a[k] - b[k] for k in range(3)]; tz = [c[k] - d[k] for k in range(3)]
        n = (tz[1] * tx[2] - tz[2] * tx[1], tz[2] * tx[0] - tz[0] * tx[2], tz[0] * tx[1] - tz[1] * tx[0])
        if n[1] < 0:
            n = tuple(-c for c in n)
        length = sum(c * c for c in n) ** 0.5
        return tuple(c / length for c in n)

    start = len(paint["vertices"])
    for j in range(nz + 1):
        for i in range(nx + 1):
            paint["vertices"].append(grid[j][i])
            paint["normals"].append(normal(i, j))
    index = lambda i, j: start + j * (nx + 1) + i
    faces = []
    for j in range(nz):
        for i in range(nx):
            a, b, c, d = index(i, j), index(i + 1, j), index(i + 1, j + 1), index(i, j + 1)
            # Unity's front faces are clockwise seen from above (+Y): rear-left, front-left, front-right.
            paint["triangles"] += [a, d, c, a, c, b]
            faces.append((a - start, b - start, c - start, d - start))
    print("Added a bonnet: z %.2f..%.2f, height %.2f..%.2f m" % (rear, front, min(tops) - BONNET_DROP, max(tops) - BONNET_DROP + BONNET_CROWN))
    return [v for row in grid for v in row], faces


def render_icon(objects, path):
    # A 128x128 three-quarter view with the flat colours, lit by a sun, on a transparent background.
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH" and obj not in objects:
            obj.hide_render = True
    for obj in objects:
        for slot in obj.material_slots:
            if slot.material is None:
                continue
            slot.material.use_nodes = True
            nodes, links = slot.material.node_tree.nodes, slot.material.node_tree.links
            nodes.clear()
            bsdf = nodes.new("ShaderNodeBsdfPrincipled")
            bsdf.inputs["Base Color"].default_value = (*COLOURS.get(slot.material.name, DEFAULT), 1.0)
            bsdf.inputs["Roughness"].default_value = 0.35
            links.new(bsdf.outputs[0], nodes.new("ShaderNodeOutputMaterial").inputs["Surface"])
            if slot.material.name in SKIP_MATERIALS:
                slot.material.node_tree.nodes.clear()
    scene = bpy.context.scene
    sun = bpy.data.objects.new("IconSun", bpy.data.lights.new("IconSun", "SUN"))
    sun.data.energy = 4.0
    sun.rotation_euler = (math.radians(50), 0.0, math.radians(30))
    scene.collection.objects.link(sun)
    if scene.world is None:
        scene.world = bpy.data.worlds.new("IconWorld")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.6
    camera = bpy.data.objects.new("IconCamera", bpy.data.cameras.new("IconCamera"))
    scene.collection.objects.link(camera)
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 6.0
    distance = 20.0
    camera.location = (distance * 0.6, -distance * 0.65, distance * 0.45)
    direction = -camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.camera = camera
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 32
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = 128
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


main()
