using UnityEngine;
using UnityEditor;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;
using Random = UnityEngine.Random;

[System.Serializable]
public struct InstanceData
{
    public float3 position;
    public float3 normal;
}

[ExecuteInEditMode]
public class SplatmapInstancePlacer : MonoBehaviour
{
    [System.Serializable]
    public class ChannelConfig
    {
        public int channelIndex; // 0=R, 1=G, 2=B, 3=A
        public List<PrefabWithWeight> prefabs = new List<PrefabWithWeight>();
        public float densityPerSqM = 10f;
        public float valueThreshold = 0.5f;
        public Vector2 scaleMinMax = new Vector2(0.8f, 1.2f);
        public Vector3 minRotation = new Vector3(0f, 0f, -5f);
        public Vector3 maxRotation = new Vector3(10f, 360f, 5f);
		public int materialIndex = 0; // Индекс материала (0 — первый)
		public int uvChannel = 0;     // Индекс UV-сета (0 — первый)
    }

    [System.Serializable]
    public class PrefabWithWeight
    {
        public GameObject prefab;
        public float weight = 1f;
    }

    public List<ChannelConfig> channels = new List<ChannelConfig>();
    public float maxSlopeAngle = 45f;
    public int sampleCount = 10000;
    public float kdeBandwidth = 0.05f;
	public int materialIndex = 0; // Индекс материала (0 — первый)
	public int uvChannel = 0;     // Индекс UV-сета (0 — первый)

    private MeshFilter meshFilter;
    private Texture2D splatmap;
    private Transform instancesParent;

    [ContextMenu("Generate Instances")]
    public void GenerateInstances()
    {
        ClearInstances();
        Initialize();

        if (meshFilter == null || splatmap == null)
        {
            Debug.LogError("MeshFilter or _SplatMap texture not found!");
            return;
        }

        Undo.RegisterCompleteObjectUndo(this, "Generate Instances");

        Mesh mesh = meshFilter.sharedMesh;
        float meshArea = CalculateMeshArea(mesh);

        NativeArray<Vector3> vertices = new NativeArray<Vector3>(mesh.vertices, Allocator.TempJob);
        NativeArray<Vector3> normals = new NativeArray<Vector3>(mesh.normals, Allocator.TempJob);
        NativeArray<int> triangles = new NativeArray<int>(mesh.triangles, Allocator.TempJob);
		Vector2[] meshUVs = null;
		switch (uvChannel)
		{
			case 0: meshUVs = mesh.uv; break;
			case 1: meshUVs = mesh.uv2; break;
			case 2: meshUVs = mesh.uv3; break;
			case 3: meshUVs = mesh.uv4; break;
			// Добавьте больше, если используете uv5-uv8 (Unity 2018+)
			default: meshUVs = mesh.uv; break;
		}
		if (meshUVs == null || meshUVs.Length == 0)
		{
			Debug.LogError($"UV channel {uvChannel} is empty!");
			return;
		}
		NativeArray<Vector2> uvs = new NativeArray<Vector2>(meshUVs, Allocator.TempJob);
        NativeArray<Color> splatPixels = new NativeArray<Color>(splatmap.GetPixels(), Allocator.TempJob);

        Dictionary<int, NativeList<InstanceData>> outputs = new Dictionary<int, NativeList<InstanceData>>();
        List<JobHandle> jobHandles = new List<JobHandle>();

        foreach (var config in channels)
        {
            int totalInstances = Mathf.RoundToInt(meshArea * config.densityPerSqM);

            NativeList<InstanceData> outputList = new NativeList<InstanceData>(Allocator.TempJob);
            outputs[config.channelIndex] = outputList;

            var job = new PlacementJob
            {
                Vertices = vertices,
                Normals = normals,
                Triangles = triangles,
                UVs = uvs,
                Splatmap = splatPixels,
                SplatmapSize = new int2(splatmap.width, splatmap.height),
                SampleCount = sampleCount,
                ChannelIndex = config.channelIndex,
                ValueThreshold = config.valueThreshold,
                MaxSlopeAngle = maxSlopeAngle,
                TotalInstances = totalInstances,
                KdeBandwidth = kdeBandwidth,
                Output = outputList
            };
            jobHandles.Add(job.Schedule());
        }

		if (jobHandles.Count > 0)
		{
			using (var handleArray = new NativeArray<JobHandle>(jobHandles.ToArray(), Allocator.Temp))
			{
				JobHandle combinedHandle = JobHandle.CombineDependencies(handleArray);
				combinedHandle.Complete();
			}
		}

        foreach (var kvp in outputs)
        {
            var config = channels.Find(c => c.channelIndex == kvp.Key);
            var outputList = kvp.Value;
            int target = Mathf.RoundToInt(meshArea * config.densityPerSqM);
            Debug.Log($"Channel {config.channelIndex}: Placed {outputList.Length} instances (target: {target})");
            if (outputList.Length < target)
            {
                Debug.LogWarning($"Not enough instances placed for channel {config.channelIndex}. Try increasing sampleCount.");
            }

            CreatePrefabInstances(config, outputList);

            outputList.Dispose();
        }

        vertices.Dispose();
        normals.Dispose();
        triangles.Dispose();
        uvs.Dispose();
        splatPixels.Dispose();
    }

    [ContextMenu("Clear Instances")]
    public void ClearInstances()
    {
        if (instancesParent != null)
        {
            Undo.RegisterCompleteObjectUndo(instancesParent.gameObject, "Clear Instances");
            while (instancesParent.childCount > 0)
            {
                Undo.DestroyObjectImmediate(instancesParent.GetChild(0).gameObject);
            }
        }
    }

    private void Initialize()
    {
        meshFilter = GetComponent<MeshFilter>();
		var renderer = GetComponent<MeshRenderer>();
		if (renderer != null && renderer.sharedMaterials.Length > materialIndex)
		{
			var mat = renderer.sharedMaterials[materialIndex];
			if (mat != null)
				splatmap = (Texture2D)mat.GetTexture("_SplatMap");
		}
        instancesParent = transform.Find("Instances");
        if (instancesParent == null)
        {
            instancesParent = new GameObject("Instances").transform;
            instancesParent.parent = transform;
            instancesParent.localPosition = Vector3.zero;
            instancesParent.localRotation = Quaternion.identity;
        }
    }

    private void PopulateGrassData(ChannelConfig config, NativeList<InstanceData> outputList)
    {
        // Удалено: больше не используется GrassInstanceData
    }

    private void CreatePrefabInstances(ChannelConfig config, NativeList<InstanceData> outputList)
    {
        float totalWeight = 0f;
        foreach (var pw in config.prefabs) totalWeight += pw.weight;

        if (instancesParent == null)
        {
            Debug.LogError("instancesParent is null! Cannot create instances.");
            return;
        }

        for (int i = 0; i < outputList.Length; i++)
        {
            var data = outputList[i];

            float rand = Random.Range(0f, totalWeight);
            GameObject selectedPrefab = null;
            float cumulative = 0f;
            foreach (var pw in config.prefabs)
            {
                cumulative += pw.weight;
                if (rand <= cumulative)
                {
                    selectedPrefab = pw.prefab;
                    break;
                }
            }

            if (selectedPrefab == null)
            {
                Debug.LogWarning("Selected prefab is null! Skipping instance creation.");
                continue;
            }

            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(selectedPrefab, instancesParent);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"PrefabUtility.InstantiatePrefab threw: {e.Message}");
                continue;
            }
            if (instance == null)
            {
                Debug.LogError("PrefabUtility.InstantiatePrefab returned null! Skipping instance creation.");
                continue;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Create Instance");

            instance.transform.position = transform.TransformPoint(data.position);

            Quaternion rot = Quaternion.FromToRotation(Vector3.up, data.normal);
            rot *= Quaternion.Euler(
                Random.Range(config.minRotation.x, config.maxRotation.x),
                Random.Range(config.minRotation.y, config.maxRotation.y),
                Random.Range(config.minRotation.z, config.maxRotation.z)
            );
            instance.transform.rotation = transform.rotation * rot;

            float scale = Random.Range(config.scaleMinMax.x, config.scaleMinMax.y);
            instance.transform.localScale = Vector3.one * scale;
        }
    }

    private float CalculateMeshArea(Mesh mesh)
    {
        float area = 0f;
        var tris = mesh.triangles;
        var verts = mesh.vertices;
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 v0 = verts[tris[i]];
            Vector3 v1 = verts[tris[i + 1]];
            Vector3 v2 = verts[tris[i + 2]];
            area += Vector3.Cross(v1 - v0, v2 - v0).magnitude * 0.5f;
        }
        return area;
    }
}

[BurstCompile]
public struct PlacementJob : IJob
{
    [ReadOnly] public NativeArray<Vector3> Vertices;
    [ReadOnly] public NativeArray<Vector3> Normals;
    [ReadOnly] public NativeArray<int> Triangles;
    [ReadOnly] public NativeArray<Vector2> UVs;
    [ReadOnly] public NativeArray<Color> Splatmap;
    public int2 SplatmapSize;
    public int SampleCount;
    public int ChannelIndex;
    public float ValueThreshold;
    public float MaxSlopeAngle;
    public int TotalInstances;
    public float KdeBandwidth;

    public NativeList<InstanceData> Output;

    public void Execute()
    {
        Unity.Mathematics.Random rand = new Unity.Mathematics.Random(12345u);

        int placedCount = 0;
        int attempts = 0;
        int maxAttempts = SampleCount * 10;

        while (placedCount < TotalInstances && attempts < maxAttempts)
        {
            attempts++;
            float2 uv = rand.NextFloat2(new float2(0, 0), new float2(1, 1));

            if (!TryGetPositionAndNormal(uv, out float3 pos, out float3 normal)) continue;

            int px = math.clamp((int)(uv.x * SplatmapSize.x), 0, SplatmapSize.x - 1);
            int py = math.clamp((int)(uv.y * SplatmapSize.y), 0, SplatmapSize.y - 1);
            Color splatColor = Splatmap[py * SplatmapSize.x + px];
            float channelValue = GetChannelValue(splatColor, ChannelIndex);

            if (channelValue < ValueThreshold) continue;

            float angle = math.degrees(math.acos(math.dot(math.normalize(normal), new float3(0, 1, 0))));
            if (angle > MaxSlopeAngle) continue;

            float density = GaussianKernel(channelValue, KdeBandwidth);
            if (rand.NextFloat() < density)
            {
                Output.Add(new InstanceData { position = pos, normal = normal });
                placedCount++;
            }
        }
    }

    private bool TryGetPositionAndNormal(float2 uv, out float3 pos, out float3 normal)
    {
        pos = float3.zero;
        normal = float3.zero;

        for (int i = 0; i < Triangles.Length; i += 3)
        {
            float2 uv0 = UVs[Triangles[i]];
            float2 uv1 = UVs[Triangles[i + 1]];
            float2 uv2 = UVs[Triangles[i + 2]];

            float3 bary = Barycentric(uv, uv0, uv1, uv2);
            if (bary.x >= 0 && bary.y >= 0 && bary.z >= 0 && math.abs(bary.x + bary.y + bary.z - 1f) < 0.001f)
            {
                pos = Vertices[Triangles[i]] * bary.x + Vertices[Triangles[i + 1]] * bary.y + Vertices[Triangles[i + 2]] * bary.z;
                normal = math.normalize(Normals[Triangles[i]] * bary.x + Normals[Triangles[i + 1]] * bary.y + Normals[Triangles[i + 2]] * bary.z);
                return true;
            }
        }
        return false;
    }

    private float3 Barycentric(float2 p, float2 a, float2 b, float2 c)
    {
        float2 v0 = b - a, v1 = c - a, v2 = p - a;
        float d00 = math.dot(v0, v0);
        float d01 = math.dot(v0, v1);
        float d11 = math.dot(v1, v1);
        float d20 = math.dot(v2, v0);
        float d21 = math.dot(v2, v1);
        float denom = d00 * d11 - d01 * d01;
        float x = (d11 * d20 - d01 * d21) / denom;
        float y = (d00 * d21 - d01 * d20) / denom;
        float z = 1f - x - y;
        return new float3(x, y, z);
    }

    private float GetChannelValue(Color color, int index)
    {
        return index switch
        {
            0 => color.r,
            1 => color.g,
            2 => color.b,
            3 => color.a,
            _ => 0f
        };
    }

    private float GaussianKernel(float value, float bandwidth)
    {
        return math.exp(-0.5f * (value * value) / (bandwidth * bandwidth)) / (bandwidth * math.sqrt(2 * math.PI));
    }
}