"""pdm_inspect.py <level file> [path prefix]: every SMR's GameObject path, mesh, materials and textures."""
import sys, UnityPy

env = UnityPy.load(sys.argv[1])
prefix = sys.argv[2] if len(sys.argv) > 2 else ""

def path(go):
    names = [go.m_Name]
    # Unity 4: m_Component is a list of (classID, PPtr) pairs
    tr = next(pp for _, pp in go.m_Component if pp.type.name == "Transform").read()
    while tr.m_Father and tr.m_Father.path_id:
        tr = tr.m_Father.read()
        names.append(tr.m_GameObject.read().m_Name)
    return "/".join(reversed(names))

for obj in env.objects:
    if obj.type.name != "SkinnedMeshRenderer":
        continue
    d = obj.read()
    go = d.m_GameObject.read()
    p = path(go)
    if not p.startswith(prefix):
        continue
    texs = []
    for m in d.m_Materials:
        try:
            mat = m.read()
            for k, v in mat.m_SavedProperties.m_TexEnvs:
                if v.m_Texture and v.m_Texture.path_id:
                    texs.append(f"{mat.m_Name}.{k}={v.m_Texture.read().m_Name}")
        except Exception as e:
            texs.append(f"ERR {e}")
    print(f"{p}\tmesh={d.m_Mesh.read().m_Name}\tenabled={d.m_Enabled}\t{texs}")
