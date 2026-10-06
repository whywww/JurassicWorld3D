using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace JurassicWorld
{
    // Serialized ownership data only. No runtime behaviour or editor-assembly component in the scene.
    public sealed class ForestRegionBackup : MonoBehaviour
    {
        [Serializable] public sealed class TerrainTreeRecord
        {
            public int index, prototypeIndex;
            public Vector3 position;
            public float widthScale, heightScale, rotation;
            public Color32 color, lightmapColor;
            public TerrainTreeRecord(int index, TreeInstance tree)
            {
                this.index = index; prototypeIndex = tree.prototypeIndex; position = tree.position;
                widthScale = tree.widthScale; heightScale = tree.heightScale; rotation = tree.rotation;
                color = tree.color; lightmapColor = tree.lightmapColor;
            }
            public TreeInstance ToInstance() => new TreeInstance { prototypeIndex = prototypeIndex, position = position,
                widthScale = widthScale, heightScale = heightScale, rotation = rotation, color = color, lightmapColor = lightmapColor };
        }
        [Serializable] public sealed class SceneTreeRecord
        {
            public GameObject original;
            public Transform parent;
            public int sibling;
            public Vector3 localPosition, localScale;
            public Quaternion localRotation;
        }
        public Terrain terrain;
        public int originalTerrainCount;
        public TerrainTreeRecord[] removedTrees = Array.Empty<TerrainTreeRecord>();
        public SceneTreeRecord[] originalSceneTrees = Array.Empty<SceneTreeRecord>();
        public float northwestAreaFraction;
        public bool applied;
        public int layoutVersion, autumnBaseline, autumnRetained;

        public TreeInstance[] RestoreTreeArray(TreeInstance[] remaining)
        {
            if (remaining.Length + removedTrees.Length != originalTerrainCount)
                throw new InvalidOperationException("Terrain trees changed after regional conversion; restore the saved checkpoint instead of overwriting them.");
            var restored = remaining.ToList();
            foreach (var record in removedTrees.OrderBy(r => r.index)) restored.Insert(record.index, record.ToInstance());
            return restored.ToArray();
        }
    }
}
