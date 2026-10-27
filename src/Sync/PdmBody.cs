using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace NunMP.Sync
{
    // Remote-player body from Power Drill Massacre's "Chick" (extracted locally by tools/pdm_export.py into
    // plugins/NunMP/pdm). Skeleton + skinned meshes rebuilt at runtime, humanoid Avatar built on the fly so the
    // nun's animator controller (walk/crouch) retargets onto it.
    public static class PdmBody
    {
        class Bone { public string name, parent; public float[] pos, rot, scale; }
        class MeshData { public string name, texture; public string[] bones; public float[][] bindposes; public float[] vertices, normals, uv; public float[][] weights; public int[][] submeshes; }
        class Model { public Bone[] skeleton; public MeshData[] meshes; }

        static GameObject template;
        static bool tried;
        static float scale = 1f;

        // Rigify names -> Unity humanoid
        static readonly Dictionary<string, string> Human = new Dictionary<string, string>
        {
            ["Hips"] = "hips", ["Spine"] = "spine", ["Chest"] = "chest", ["Neck"] = "neck", ["Head"] = "head",
            ["LeftShoulder"] = "shoulder_L", ["LeftUpperArm"] = "upper_arm_L", ["LeftLowerArm"] = "forearm_L", ["LeftHand"] = "hand_L",
            ["RightShoulder"] = "shoulder_R", ["RightUpperArm"] = "upper_arm_R", ["RightLowerArm"] = "forearm_R", ["RightHand"] = "hand_R",
            ["LeftUpperLeg"] = "thigh_L", ["LeftLowerLeg"] = "shin_L", ["LeftFoot"] = "foot_L", ["LeftToes"] = "toe_L",
            ["RightUpperLeg"] = "thigh_R", ["RightLowerLeg"] = "shin_R", ["RightFoot"] = "foot_R", ["RightToes"] = "toe_R",
        };

        static string Dir => Path.Combine(Path.GetDirectoryName(Plugin.Instance.Info.Location), "pdm");

        // Returns a fresh body with an Animator, or null if the PDM export isn't installed.
        public static GameObject Create(RuntimeAnimatorController ctrl, Material shaderFrom)
        {
            if (!template && !tried)
            {
                tried = true;
                try { template = Build(shaderFrom); }
                catch (System.Exception e) { Plugin.Log.LogWarning("PDM body unavailable: " + e.Message); }
            }
            if (!template) return null;
            var go = Object.Instantiate(template);
            go.SetActive(true);
            go.name = "PDM body";
            go.transform.localScale = Vector3.one * scale;
            var a = go.GetComponent<Animator>();
            a.runtimeAnimatorController = ctrl;
            return go;
        }

        static GameObject Build(Material shaderFrom)
        {
            var file = Path.Combine(Dir, "model.json");
            if (!File.Exists(file)) throw new FileNotFoundException("run tools/pdm_export.py (needs Power Drill Massacre)", file);
            var m = JsonConvert.DeserializeObject<Model>(File.ReadAllText(file));
            var t = new Dictionary<string, Transform>();
            GameObject root = null;
            foreach (var b in m.skeleton)
            {
                var go = new GameObject(b.name);
                if (b.parent == null) root = go; else go.transform.SetParent(t[b.parent], false);
                go.transform.localPosition = new Vector3(b.pos[0], b.pos[1], b.pos[2]);
                go.transform.localRotation = new Quaternion(b.rot[0], b.rot[1], b.rot[2], b.rot[3]);
                go.transform.localScale = new Vector3(b.scale[0], b.scale[1], b.scale[2]);
                t[b.name] = go.transform;
            }
            root.SetActive(false); // template stays hidden
            var texCache = new Dictionary<string, Texture2D>();
            foreach (var md in m.meshes)
            {
                var mesh = new Mesh { name = md.name };
                mesh.vertices = V3(md.vertices);
                if (md.normals.Length > 0) mesh.normals = V3(md.normals);
                if (md.uv.Length > 0) mesh.uv = Enumerable.Range(0, md.uv.Length / 2).Select(i => new Vector2(md.uv[i * 2], md.uv[i * 2 + 1])).ToArray();
                mesh.boneWeights = md.weights.Select(w => new BoneWeight
                {
                    boneIndex0 = (int)w[0], boneIndex1 = (int)w[1], boneIndex2 = (int)w[2], boneIndex3 = (int)w[3],
                    weight0 = w[4], weight1 = w[5], weight2 = w[6], weight3 = w[7],
                }).ToArray();
                mesh.bindposes = md.bindposes.Select(p =>
                {
                    var mx = new Matrix4x4();
                    for (int i = 0; i < 16; i++) mx[i / 4, i % 4] = p[i];
                    return mx;
                }).ToArray();
                mesh.subMeshCount = md.submeshes.Length;
                for (int i = 0; i < md.submeshes.Length; i++) mesh.SetTriangles(md.submeshes[i], i);
                mesh.RecalculateBounds();

                var host = t[md.name].gameObject;
                var smr = host.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = md.bones.Select(n => t[n]).ToArray();
                smr.rootBone = t["hips"];
                smr.updateWhenOffscreen = true;
                if (!texCache.TryGetValue(md.texture ?? "", out var tex) && md.texture != null)
                {
                    tex = new Texture2D(2, 2) { filterMode = FilterMode.Point };
                    tex.LoadImage(File.ReadAllBytes(Path.Combine(Dir, md.texture)));
                    texCache[md.texture] = tex;
                }
                var mat = shaderFrom ? new Material(shaderFrom) : new Material(Shader.Find("Standard"));
                if (tex) mat.mainTexture = tex;
                smr.sharedMaterials = Enumerable.Repeat(mat, mesh.subMeshCount).ToArray();
            }
            var hd = new HumanDescription
            {
                human = Human.Where(kv => t.ContainsKey(kv.Value)).Select(kv => new HumanBone { humanName = kv.Key, boneName = kv.Value, limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
                skeleton = m.skeleton.Select(b => new SkeletonBone { name = b.name, position = t[b.name].localPosition, rotation = t[b.name].localRotation, scale = t[b.name].localScale }).ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0, hasTranslationDoF = false,
            };
            var avatar = AvatarBuilder.BuildHumanAvatar(root, hd);
            if (!avatar.isValid || !avatar.isHuman) throw new System.Exception("avatar build failed (valid=" + avatar.isValid + ")");
            // The exported rig comes out tiny (source scale lives outside the Chick subtree): normalise to adult height.
            var headY = t["head"].position.y - root.transform.position.y;
            scale = headY > 0.01f ? 1.6f / headY : 1f;
            Plugin.Log.LogInfo($"PDM head height {headY:0.###} -> scale {scale:0.##}");
            var anim = root.AddComponent<Animator>();
            anim.avatar = avatar;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Object.DontDestroyOnLoad(root);
            Plugin.Log.LogInfo("PDM body ready (" + m.meshes.Length + " meshes)");
            return root;
        }

        static Vector3[] V3(float[] f) => Enumerable.Range(0, f.Length / 3).Select(i => new Vector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2])).ToArray();
    }
}
