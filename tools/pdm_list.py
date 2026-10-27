"""pdm_list.py <Data dir>: list SkinnedMeshRenderers (name, mesh, bone count, materials) and Animators/Avatars."""
import sys, os, UnityPy

data = sys.argv[1]
files = [os.path.join(data, f) for f in os.listdir(data) if f.startswith(("level", "sharedassets", "resources", "mainData")) and not f.endswith(".resS")]
for f in sorted(files):
    env = UnityPy.load(f)
    for obj in env.objects:
        t = obj.type.name
        if t not in ("SkinnedMeshRenderer", "Avatar", "Animator"):
            continue
        try:
            d = obj.read()
            if t == "SkinnedMeshRenderer":
                go = d.m_GameObject.read()
                mesh = d.m_Mesh.read() if d.m_Mesh else None
                mats = []
                for m in d.m_Materials:
                    try: mats.append(m.read().m_Name)
                    except Exception: mats.append("?")
                print(f"{os.path.basename(f)}\tSMR\t{go.m_Name}\tmesh={mesh.m_Name if mesh else None}\tverts={getattr(getattr(mesh, "m_VertexData", None), "m_VertexCount", "?") if mesh else 0}\tbones={len(d.m_Bones)}\tmats={mats}")
            elif t == "Avatar":
                print(f"{os.path.basename(f)}\tAvatar\t{d.m_Name}")
            else:
                go = d.m_GameObject.read()
                print(f"{os.path.basename(f)}\tAnimator\t{go.m_Name}")
        except Exception as e:
            print(f"{os.path.basename(f)}\t{t}\tERR {e}")
