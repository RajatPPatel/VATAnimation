using System;
using System.Collections.Generic;
using UnityEngine;

public class GPUInstancedSpawner : MonoBehaviour
{
    [Serializable]
    public class AudiencePrefab
    {
        public Mesh mesh;
        public Material material;
        [Range(0f, 1f)]
        public float spawnWeight = 1f; // Higher weight = more likely to spawn
    }

    [Serializable]
    public class SpawnLine
    {
        public Transform pointA;
        public Transform pointB;
        [Tooltip("Distance between each person along this line")]
        public float spacing = 1f;
        [Tooltip("Random offset perpendicular to the line")]
        public float lateralOffset = 0.1f;
    }

    [Header("Audience Prefabs")]
    [Tooltip("List of different audience meshes/materials to randomly spawn")]
    public List<AudiencePrefab> audiencePrefabs = new List<AudiencePrefab>();

    [Header("Spawn Lines")]
    [Tooltip("List of point pairs to spawn audience between")]
    public List<SpawnLine> spawnLines = new List<SpawnLine>();

    [Header("Rendering Settings")]
    public UnityEngine.Rendering.ShadowCastingMode shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    public bool receiveShadows = true;

    [Header("Debug")]
    public bool showGizmos = true;
    public Color gizmoColor = Color.cyan;

    // Dictionary to store matrices per prefab type (mesh + material combo)
    private Dictionary<int, List<Matrix4x4>> matricesByPrefab = new Dictionary<int, List<Matrix4x4>>();
    private Dictionary<int, Matrix4x4[]> bakedMatrices = new Dictionary<int, Matrix4x4[]>();

    private void Start()
    {
        GenerateAudiencePositions();
    }

    [ContextMenu("Regenerate Audience")]
    public void GenerateAudiencePositions()
    {
        if (audiencePrefabs == null || audiencePrefabs.Count == 0)
        {
            Debug.LogWarning("GPUInstancedSpawner: No audience prefabs assigned!");
            return;
        }

        if (spawnLines == null || spawnLines.Count == 0)
        {
            Debug.LogWarning("GPUInstancedSpawner: No spawn lines defined!");
            return;
        }

        // Clear previous data
        matricesByPrefab.Clear();
        bakedMatrices.Clear();

        // Initialize lists for each prefab
        for (int i = 0; i < audiencePrefabs.Count; i++)
        {
            if (audiencePrefabs[i].mesh != null && audiencePrefabs[i].material != null)
            {
                matricesByPrefab[i] = new List<Matrix4x4>();
            }
        }

        // Calculate total weight for random selection
        float totalWeight = 0f;
        foreach (var prefab in audiencePrefabs)
        {
            if (prefab.mesh != null && prefab.material != null)
                totalWeight += prefab.spawnWeight;
        }

        // Generate positions along each spawn line
        foreach (var line in spawnLines)
        {
            if (line.pointA == null || line.pointB == null)
            {
                Debug.LogWarning("GPUInstancedSpawner: Spawn line has missing points!");
                continue;
            }

            Vector3 start = line.pointA.position;
            Vector3 end = line.pointB.position;
            Vector3 direction = (end - start).normalized;
            float distance = Vector3.Distance(start, end);
            int personCount = Mathf.FloorToInt(distance / line.spacing);

            // Calculate perpendicular direction for lateral offset
            Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;

            for (int i = 0; i <= personCount; i++)
            {
                // Position along the line
                Vector3 position = start + direction * (i * line.spacing);

                // Add random lateral offset
                if (line.lateralOffset > 0)
                {
                    float offset = UnityEngine.Random.Range(-line.lateralOffset, line.lateralOffset);
                    position += perpendicular * offset;
                }

                // Calculate rotation
                Quaternion rotation;
                // if (line.faceCenter && line.lookAtTarget != null)
                // {
                //     Vector3 lookDir = (line.lookAtTarget.position - position).normalized;
                //     lookDir.y = 0; // Keep upright
                //     if (lookDir != Vector3.zero)
                //         rotation = Quaternion.LookRotation(lookDir);
                //     else
                //         rotation = Quaternion.identity;
                // }
                // else
                // {
                //     // Random rotation
                //     rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
                // }
                rotation = line.pointA.rotation;

                // Select random prefab based on weights
                int prefabIndex = GetRandomPrefabIndex(totalWeight);
                if (prefabIndex >= 0 && matricesByPrefab.ContainsKey(prefabIndex))
                {
                    Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
                    matricesByPrefab[prefabIndex].Add(matrix);
                }
            }
        }

        // Bake lists to arrays for rendering
        foreach (var kvp in matricesByPrefab)
        {
            if (kvp.Value.Count > 0)
            {
                bakedMatrices[kvp.Key] = kvp.Value.ToArray();
            }
        }

        int totalCount = 0;
        foreach (var arr in bakedMatrices.Values)
            totalCount += arr.Length;

        Debug.Log($"GPUInstancedSpawner: Generated {totalCount} audience members across {bakedMatrices.Count} prefab types");
    }

    private int GetRandomPrefabIndex(float totalWeight)
    {
        float randomValue = UnityEngine.Random.Range(0f, totalWeight);
        float currentWeight = 0f;

        for (int i = 0; i < audiencePrefabs.Count; i++)
        {
            if (audiencePrefabs[i].mesh == null || audiencePrefabs[i].material == null)
                continue;

            currentWeight += audiencePrefabs[i].spawnWeight;
            if (randomValue <= currentWeight)
                return i;
        }

        // Fallback to first valid prefab
        for (int i = 0; i < audiencePrefabs.Count; i++)
        {
            if (audiencePrefabs[i].mesh != null && audiencePrefabs[i].material != null)
                return i;
        }

        return -1;
    }

    private void Update()
    {
        RenderAllInstances();
    }

    private void RenderAllInstances()
    {
        foreach (var kvp in bakedMatrices)
        {
            int prefabIndex = kvp.Key;
            Matrix4x4[] matrices = kvp.Value;

            if (matrices == null || matrices.Length == 0)
                continue;

            if (prefabIndex >= audiencePrefabs.Count)
                continue;

            var prefab = audiencePrefabs[prefabIndex];
            if (prefab.mesh == null || prefab.material == null)
                continue;

            RenderParams rp = new RenderParams(prefab.material)
            {
                shadowCastingMode = this.shadowCastingMode,
                receiveShadows = this.receiveShadows
            };

            // RenderMeshInstanced has a limit of 1023 instances per call
            int batchSize = 1023;
            for (int i = 0; i < matrices.Length; i += batchSize)
            {
                int count = Mathf.Min(batchSize, matrices.Length - i);

                if (i == 0 && count == matrices.Length)
                {
                    // Can render all at once
                    Graphics.RenderMeshInstanced(rp, prefab.mesh, 0, matrices, count);
                }
                else
                {
                    // Need to slice the array for batching
                    Matrix4x4[] batch = new Matrix4x4[count];
                    Array.Copy(matrices, i, batch, 0, count);
                    Graphics.RenderMeshInstanced(rp, prefab.mesh, 0, batch, count);
                }
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showGizmos || spawnLines == null)
            return;

        Gizmos.color = gizmoColor;

        foreach (var line in spawnLines)
        {
            if (line.pointA == null || line.pointB == null)
                continue;

            // Draw line between points
            Gizmos.DrawLine(line.pointA.position, line.pointB.position);

            // Draw spheres at points
            Gizmos.DrawWireSphere(line.pointA.position, 0.3f);
            Gizmos.DrawWireSphere(line.pointB.position, 0.3f);

            // Draw spawn positions preview
            Vector3 start = line.pointA.position;
            Vector3 end = line.pointB.position;
            Vector3 direction = (end - start).normalized;
            float distance = Vector3.Distance(start, end);
            int personCount = Mathf.FloorToInt(distance / line.spacing);

            Gizmos.color = Color.yellow;
            for (int i = 0; i <= personCount; i++)
            {
                Vector3 position = start + direction * (i * line.spacing);
                Gizmos.DrawWireSphere(position, 0.15f);
            }
            Gizmos.color = gizmoColor;
        }
    }
#endif
}
