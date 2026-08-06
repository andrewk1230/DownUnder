using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// The TerrainDetails meshes were extracted from the source GLBs with the grid
// layout of the export scene still baked into their vertices, so their local
// origin sits well below (and off to the side of) the geometry. Dragging the
// prefab into the scene hides this because Unity drops the object using its
// renderer bounds, but terrain detail painting puts the mesh *origin* on the
// terrain surface, so every painted instance floats by bounds.min.y.
//
// This recenters the meshes on their own base (min.y = 0, centered in XZ) and
// compensates any already-placed instances in the open scenes so they don't move.
public static class FixDetailMeshPivots
{
    const string MeshFolder = "Assets/TerrainDetails/Meshes";
    const string PrefabFolder = "Assets/TerrainDetails/Prefabs";
    const float Epsilon = 0.005f;

    [MenuItem("Tools/Terrain Details/Fix Detail Mesh Pivots")]
    public static void Run()
    {
        var meshes = new List<Mesh>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                if (!AssetDatabase.GetAssetPath(mesh).StartsWith(MeshFolder)) continue;
                if (!meshes.Contains(mesh)) meshes.Add(mesh);
            }
        }

        // Offset that must be subtracted from each mesh's vertices.
        var offsets = new Dictionary<Mesh, Vector3>();
        foreach (var mesh in meshes)
        {
            var b = mesh.bounds;
            var offset = new Vector3(b.center.x, b.min.y, b.center.z);
            if (offset.magnitude > Epsilon) offsets[mesh] = offset;
        }

        if (offsets.Count == 0)
        {
            Debug.Log("[FixDetailMeshPivots] All detail meshes are already based at their origin. Nothing to do.");
            return;
        }

        // Compensate instances already placed by hand, so they stay put.
        int moved = 0;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || !offsets.TryGetValue(mf.sharedMesh, out var offset)) continue;
                    var t = mf.transform;
                    Undo.RecordObject(t, "Fix Detail Mesh Pivots");
                    t.position += t.TransformVector(offset);
                    EditorUtility.SetDirty(t);
                    moved++;
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        // Rebase the meshes themselves.
        foreach (var pair in offsets)
        {
            var mesh = pair.Key;
            var offset = pair.Value;

            var verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] -= offset;
            mesh.vertices = verts;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            Debug.Log($"[FixDetailMeshPivots] {mesh.name}: rebased by {-offset} (was floating {offset.y:0.###} above its origin).");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[FixDetailMeshPivots] Rebased {offsets.Count} mesh(es); compensated {moved} placed instance(s). " +
                  "Repaint or re-add the detail prototypes on the terrain to see painted details snap down.");
    }
}
