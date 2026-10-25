# Prints the transform hierarchy (local position, rotation, scale, components) of every prefab or scene
# object tree in the game's asset files that contains a GameObject with the given name.
# Usage: python -I dump_prefab.py <Arcadius_Data> <GameObject name> [asset file, default sharedassets0.assets]
# Needs UnityPy (pip install UnityPy, in a venv); MonoBehaviours are shown as MB:<script class>.
import os
import sys

import UnityPy

data, name = sys.argv[1], sys.argv[2]
asset_file = sys.argv[3] if len(sys.argv) > 3 else "sharedassets0.assets"
env = UnityPy.load(os.path.join(data, asset_file))


def components(go):
    for c in go.m_Component:
        yield (c.component if hasattr(c, "component") else c[1]).deref()


def transform_of(go):
    return next(o.read() for o in components(go) if o.type.name in ("Transform", "RectTransform"))


def describe(o):
    if o.type.name != "MonoBehaviour":
        return o.type.name
    try:
        return "MB:" + o.read().m_Script.read().m_ClassName
    except Exception:
        return "MonoBehaviour"


def walk(t, depth):
    go = t.m_GameObject.read()
    p, r, s = t.m_LocalPosition, t.m_LocalRotation, t.m_LocalScale
    kinds = [describe(o) for o in components(go) if o.type.name != "Transform"]
    print("  " * depth + f"{go.m_Name} pos=({p.x:.3f}, {p.y:.3f}, {p.z:.3f}) "
          f"rot=({r.x:.3f}, {r.y:.3f}, {r.z:.3f}, {r.w:.3f}) scale={s.x:.3f} [{', '.join(kinds)}]")
    for child in t.m_Children:
        walk(child.read(), depth + 1)


seen = set()
for obj in env.objects:
    if obj.type.name != "GameObject" or obj.read().m_Name != name:
        continue
    t = transform_of(obj.read())
    while t.m_Father.path_id != 0:
        t = t.m_Father.read()
    if t.m_GameObject.path_id in seen:
        continue
    seen.add(t.m_GameObject.path_id)
    print(f"===== {asset_file}")
    walk(t, 0)
if not seen:
    sys.exit(f"No GameObject named {name!r} in {asset_file}")
