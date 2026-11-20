using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelEasily;

internal sealed class GaugeMesh
{
    private const float HalfSize = 0.15f;
    private const float Lift = 0.05f;

    private readonly GameObject root = new("LevelEasilyPreview");
    private readonly Mesh mesh = new();
    private readonly List<Vector3> vertices = new();
    private readonly List<Color> colours = new();
    private readonly List<int> indices = new();

    public GaugeMesh()
    {
        Object.DontDestroyOnLoad(root);
        mesh.MarkDynamic();
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        var material = new Material(Shader.Find("Hidden/Internal-Colored"));
        material.SetInt("_ZTest", (int)CompareFunction.Always);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_Cull", (int)CullMode.Off);
        root.AddComponent<MeshRenderer>().sharedMaterial = material;
        root.SetActive(false);
    }

    public void SetVisible(bool visible) => root.SetActive(visible);

    public void Rebuild(IEnumerable<(Vector3 point, Color colour)> markers)
    {
        vertices.Clear();
        colours.Clear();
        indices.Clear();
        foreach (var (point, colour) in markers)
        {
            var centre = point + Vector3.up * Lift;
            indices.Add(vertices.Count); vertices.Add(centre + new Vector3(-HalfSize, 0, -HalfSize));
            indices.Add(vertices.Count); vertices.Add(centre + new Vector3(-HalfSize, 0, HalfSize));
            indices.Add(vertices.Count); vertices.Add(centre + new Vector3(HalfSize, 0, HalfSize));
            indices.Add(vertices.Count); vertices.Add(centre + new Vector3(HalfSize, 0, -HalfSize));
            colours.AddRange(new[] { colour, colour, colour, colour });
        }
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colours);
        mesh.SetIndices(indices, MeshTopology.Quads, 0);
        mesh.RecalculateBounds();
    }
}
