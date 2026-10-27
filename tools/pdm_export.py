"""pdm_export.py <PDM Data dir> <out dir>
Exports Power Drill Massacre's player body (System/Cut Scene/Player Double/Chick) from the user's own install:
model.json (skeleton rest pose + skinned meshes) and the albedo PNGs. Read at runtime by NunMP's PdmBody.
Never redistribute the output: each player extracts it from their own copy."""
import json, os, sys
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

data, out = sys.argv[1], sys.argv[2]
ROOT = "System/Cut Scene/Player Double/Chick"
os.makedirs(out, exist_ok=True)
env = UnityPy.load(os.path.join(data, "level0"))

def comp(go, name):
    return next((pp for _, pp in go.m_Component if pp.type.name == name), None)

def path_of(tr):
    names = []
    while tr:
        names.append(tr.m_GameObject.read().m_Name)
        tr = tr.m_Father.read() if tr.m_Father and tr.m_Father.path_id else None
    return "/".join(reversed(names))

# find the Chick root transform
root = None
for obj in env.objects:
    if obj.type.name == "Transform":
        t = obj.read()
        if path_of(t) == ROOT:
            root = t
            break
assert root, "Player Double/Chick not found (different PDM version?)"

bones = []  # skeleton rest pose, parent-first
def walk(t, parent):
    go = t.m_GameObject.read()
    p, r, s = t.m_LocalPosition, t.m_LocalRotation, t.m_LocalScale
    bones.append({"name": go.m_Name, "parent": parent,
                  "pos": [p.x, p.y, p.z], "rot": [r.x, r.y, r.z, r.w], "scale": [s.x, s.y, s.z]})
    for c in t.m_Children:
        walk(c.read(), go.m_Name)
walk(root, None)

meshes, textures = [], set()
for obj in env.objects:
    if obj.type.name != "SkinnedMeshRenderer":
        continue
    smr = obj.read()
    go = smr.m_GameObject.read()
    if not path_of(comp(go, "Transform").read()).startswith(ROOT + "/"):
        continue
    mesh = smr.m_Mesh.read()
    h = MeshHandler(mesh)
    h.process()
    tex = None
    mat = smr.m_Materials[0].read()
    for k, v in mat.m_SavedProperties.m_TexEnvs:
        if getattr(k, "name", k) == "_MainTex" and v.m_Texture and v.m_Texture.path_id:
            t2d = v.m_Texture.read()
            tex = t2d.m_Name + ".png"
            if tex not in textures:
                t2d.image.save(os.path.join(out, tex))
                textures.add(tex)
    subs = []
    for i in range(len(mesh.m_SubMeshes)):
        subs.append([int(x) for tri in h.get_triangles()[i] for x in tri])
    # MeshHandler gives parallel tuples: m_BoneIndices[i] = (i0..i3), m_BoneWeights[i] = (w0..w3)
    bw = [list(i) + list(w) for i, w in zip(h.m_BoneIndices or [], h.m_BoneWeights or [])]
    meshes.append({
        "name": go.m_Name, "texture": tex,
        "bones": [b.read().m_GameObject.read().m_Name for b in smr.m_Bones],
        "bindposes": [[m.e00, m.e01, m.e02, m.e03, m.e10, m.e11, m.e12, m.e13, m.e20, m.e21, m.e22, m.e23, m.e30, m.e31, m.e32, m.e33] for m in mesh.m_BindPose],
        "vertices": [c for v in h.m_Vertices for c in v],
        "normals": [c for v in (h.m_Normals or []) for c in v],
        "uv": [c for v in (h.m_UV0 or []) for c in v],
        "weights": bw,
        "submeshes": subs,
    })
    print(f"mesh {go.m_Name}: {len(h.m_Vertices)} verts, {len(smr.m_Bones)} bones, tex={tex}")

json.dump({"source": "Power Drill Massacre 0.04 (" + ROOT + ")", "skeleton": bones, "meshes": meshes},
          open(os.path.join(out, "model.json"), "w"))
print(f"skeleton {len(bones)} transforms -> {out}")
