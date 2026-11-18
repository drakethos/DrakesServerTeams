using System;
using System.Collections.Generic;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// The figure in the middle of a Team Ward: the original guard stone, or a low-poly stone animal made in Blender (Models/make_team_figures.py).
/// Every figure is one mesh with the ward's stone material and its glow material (the eyes), so the obsidian look, the team glow and the
/// power dimming work on all of them. A team picks its figure with <c>teams setfigure</c> or the admin window (TeamDefinition.Figure).
/// </summary>
public static class WardFigures
{
    public const string Default = "stone";

    /// <summary>The figures a team can choose, in the order the admin window shows them.</summary>
    public static readonly string[] Ids = { "stone", "wolf", "dragon", "stag", "freya", "thor" };

    /// <summary>Figures borrowed from the game's own statues (decorations in the Hammer's Statues tab): read from the prefab at runtime, nothing is shipped.</summary>
    static readonly Dictionary<string, string> Borrowed = new() { ["freya"] = "StatueFreya", ["thor"] = "StatueThor" };

    const float PlinthTop = 0.21f;
    const float MaxHeight = 1.9f;   // the halos orbit above the figure
    const float MaxWidth = 1.3f;    // stays inside the plinth

    static readonly Dictionary<string, List<GlbModel.ModelPart>> Cache = new();

    /// <summary>The figure name in lower case; anything unknown is the original stone.</summary>
    public static string Normalize(string? id)
    {
        foreach (var s in Ids)
            if (string.Equals(s, id?.Trim(), StringComparison.OrdinalIgnoreCase))
                return s;
        return Default;
    }

    /// <summary>"Stone", "Wolf" ... for buttons.</summary>
    public static string Title(string id) => id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id.Substring(1);

    /// <summary>Put the figure's parts under a new child of the ward and return it, or null when the model or the ward materials are missing.</summary>
    public static GameObject? Build(Transform ward, string id)
    {
        id = Normalize(id);
        if (Borrowed.TryGetValue(id, out var prefabName))
            return BuildBorrowed(ward, id, prefabName);
        var stone = TeamWard.StoneMaterial;
        var glow = TeamWard.GlowMaterial;
        if (stone == null || glow == null)
            return null;
        if (!Cache.TryGetValue(id, out var parts))
        {
            if (!GlbModel.TryLoadParts("figure_" + id, out parts, out var error))
            {
                TeamsNet.Warn($"Ward figure '{id}' not loaded ({error}).");
                return null;
            }
            Cache[id] = parts;
        }
        var holder = new GameObject("figure_" + id + "_holder");
        holder.transform.SetParent(ward, false);
        foreach (var part in parts)
        {
            var slots = new Material[part.Materials.Length];
            for (var i = 0; i < slots.Length; i++)
                slots[i] = part.Materials[i].name.Contains("Glow") ? glow : stone;
            var go = new GameObject(part.Name);
            go.transform.SetParent(holder.transform, false);
            go.transform.localPosition = part.Position;
            go.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = slots;
        }
        return holder;
    }

    /// <summary>
    /// A statue from the game: its meshes (the nearest level of detail) with their own materials, scaled down to fit the plinth and
    /// stood on it. The materials are the game's, so there is no team tint on the stone; the ward's crystals and rune ring still carry the team color.
    /// </summary>
    static GameObject? BuildBorrowed(Transform ward, string id, string prefabName)
    {
        var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
        if (prefab == null)
        {
            TeamsNet.Warn($"Ward figure '{id}': the game has no prefab '{prefabName}'.");
            return null;
        }
        var allowed = new HashSet<Renderer>();
        var lods = prefab.GetComponent<LODGroup>();
        if (lods != null && lods.lodCount > 0)
            foreach (var r in lods.GetLODs()[0].renderers)
                if (r != null)
                    allowed.Add(r);

        var holder = new GameObject("figure_" + id + "_holder");
        holder.transform.SetParent(ward, false);
        var any = false;
        var bounds = new Bounds();
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || mf.sharedMesh == null || (allowed.Count > 0 && !allowed.Contains(mr)))
                continue;
            var m = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var go = new GameObject(mf.name);
            go.transform.SetParent(holder.transform, false);
            go.transform.localPosition = m.GetColumn(3);
            go.transform.localRotation = m.rotation;
            go.transform.localScale = m.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
            // size of the part in the prefab's space
            var b = TransformBounds(mf.sharedMesh.bounds, m);
            if (any)
                bounds.Encapsulate(b);
            else
                bounds = b;
            any = true;
        }
        if (!any)
        {
            UnityEngine.Object.Destroy(holder);
            return null;
        }
        var scale = Mathf.Min(1f, MaxHeight / Mathf.Max(0.01f, bounds.size.y), MaxWidth / Mathf.Max(0.01f, Mathf.Max(bounds.size.x, bounds.size.z)));
        holder.transform.localScale = Vector3.one * scale;
        // stand it on the plinth, centred
        holder.transform.localPosition = new Vector3(-bounds.center.x * scale, PlinthTop - bounds.min.y * scale, -bounds.center.z * scale);
        return holder;
    }

    static Bounds TransformBounds(Bounds b, Matrix4x4 m)
    {
        var result = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
        for (var i = 0; i < 8; i++)
        {
            var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            result.Encapsulate(m.MultiplyPoint3x4(corner));
        }
        return result;
    }
}
