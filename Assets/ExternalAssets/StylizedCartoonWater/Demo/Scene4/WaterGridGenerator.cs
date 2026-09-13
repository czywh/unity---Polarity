using UnityEditor;
using UnityEngine;

[ExecuteInEditMode]
public class WaterGridGenerator : MonoBehaviour
{
    [Header("Material (Main)")]
    public MeshRenderer meshRenderer;
    private Material waterMaterial;

    [Header("Grid Settings")]
    [Range(1, 50)] public int gridSizeX = 10;
    [Range(1, 50)] public int gridSizeZ = 10;

    public Vector3 tileScale = new Vector3(1f, 0.38f, 0.84f);

    [Tooltip("Planes gaps")]
    public float gap = 0f;

    public bool centerGrid = true;

    private void Reset()
    {
        meshRenderer = GetComponent<MeshRenderer>();

        if (meshRenderer != null)
            waterMaterial = meshRenderer.sharedMaterial;
    }

    [ContextMenu("Generate Water Grid")]
    public void GenerateGrid()
    {
        ClearGrid();

        if (meshRenderer != null)
            waterMaterial = meshRenderer.sharedMaterial;

        float spacingX = (10f * tileScale.x) + gap;
        float spacingZ = (10f * tileScale.z) + gap;

        for (int x = 0; x < gridSizeX; x++)
        {
            for (int z = 0; z < gridSizeZ; z++)
            {
                GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Plane);
                tile.name = $"WaterTile_{x}_{z}";
                tile.transform.SetParent(transform, false);

                Vector3 position = new Vector3(
                    x * spacingX,
                    0f,
                    z * spacingZ
                );

                if (centerGrid)
                {
                    position.x -= ((gridSizeX - 1) * spacingX) * 0.5f;
                    position.z -= ((gridSizeZ - 1) * spacingZ) * 0.5f;
                }

                tile.transform.localPosition = position;
                tile.transform.localRotation = Quaternion.identity;
                tile.transform.localScale = tileScale;

                Renderer rend = tile.GetComponent<Renderer>();
                rend.sharedMaterial = waterMaterial;
            }
        }

        Debug.Log($"Generated Grid: {gridSizeX} x {gridSizeZ} tiles");
    }

    public float GetWaveHeightAt(Vector3 worldPosition)
    {
        if (waterMaterial == null) return worldPosition.y;

        float time = Time.time;
        float waveSpeed = waterMaterial.GetFloat("_WaveSpeed");          
        float waveIntensity = waterMaterial.GetFloat("_WaveIntensity");

        Vector2 uv = new Vector2(worldPosition.x, worldPosition.z); // World Space

        //Adjust this values according the tilin in the shader
        float noise = Mathf.PerlinNoise(
            uv.x * 0.8f + time * waveSpeed,
            uv.y * 0.8f + time * waveSpeed * 0.7f
        );

        // Gradient Noise is usually smooth thats why *
        float height = (noise - 0.5f) * 2f * waveIntensity * 1.8f;   //Adjust 1.8f

        return worldPosition.y + height;
    }

    [ContextMenu("Clear Grid")]
    public void ClearGrid()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }
}

[CustomEditor(typeof(WaterGridGenerator))]
public class WaterGridGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        WaterGridGenerator generator = (WaterGridGenerator)target;

        DrawDefaultInspector();

        EditorGUILayout.Space(10);

        if (GUILayout.Button("Generate Water Grid", GUILayout.Height(35)))
        {
            generator.GenerateGrid();
        }

        if (GUILayout.Button("Clear Grid", GUILayout.Height(30)))
        {
            generator.ClearGrid();
        }
    }
}