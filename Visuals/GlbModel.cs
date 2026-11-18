using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// A small loader for the .glb models in this mod (made in Blender, embedded as resources). It reads exactly what Blender's
/// glTF export writes: one mesh, triangle primitives (one per material), positions, normals, UVs and flat material colors.
/// No textures, animation or skins. Kept inside this mod so banners and flags need no other mod or asset bundle.
/// The glTF JSON is read with a tiny parser of its own (Unity's JsonUtility cannot map these shapes).
/// </summary>
internal static class GlbModel
{
    const uint Magic = 0x46546C67;     // "glTF"
    const uint ChunkJson = 0x4E4F534A; // "JSON"
    const uint ChunkBin = 0x004E4942;  // "BIN"

    /// <summary>The loaded mesh and its materials (one per submesh), or false with a reason.</summary>
    public static bool TryLoad(string modelName, out Mesh? mesh, out Material[] materials, out string error)
    {
        mesh = null;
        materials = new Material[0];
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("DrakesServerTeams.Models." + modelName + ".glb");
            if (stream == null)
            {
                error = "model resource not found";
                return false;
            }
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return Parse(ms.ToArray(), modelName, out mesh, out materials, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static bool Parse(byte[] data, string name, out Mesh? mesh, out Material[] materials, out string error)
    {
        mesh = null;
        materials = new Material[0];
        if (!ReadFile(data, out var root, out var bin, out error))
            return false;
        var meshes = Json.List(root, "meshes");
        if (meshes.Count == 0)
        {
            error = "no mesh in the JSON";
            return false;
        }
        mesh = BuildMesh(root!, bin!, Json.Obj(meshes[0]), name, out materials);
        return true;
    }

    /// <summary>One piece of a multi-part model: its node name, mesh, materials and where its origin sits (Unity space).</summary>
    internal sealed class ModelPart
    {
        public string Name = "";
        public Mesh Mesh = null!;
        public Material[] Materials = new Material[0];
        public Vector3 Position;
    }

    /// <summary>Every mesh node of a model (the Team Ward is built from many named parts). Rotation and scale on nodes are not used.</summary>
    public static bool TryLoadParts(string modelName, out List<ModelPart> parts, out string error)
    {
        parts = new List<ModelPart>();
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DrakesServerTeams.Models." + modelName + ".glb");
            if (stream == null)
            {
                error = "model resource not found";
                return false;
            }
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            if (!ReadFile(ms.ToArray(), out var root, out var bin, out error))
                return false;
            var meshes = Json.List(root, "meshes");
            foreach (var nodeObj in Json.List(root, "nodes"))
            {
                var node = Json.Obj(nodeObj);
                var meshIndex = Json.Int(node, "mesh", -1);
                if (meshIndex < 0 || meshIndex >= meshes.Count)
                    continue;
                var part = new ModelPart { Name = Json.Str(node, "name", "part" + parts.Count) };
                part.Mesh = BuildMesh(root!, bin!, Json.Obj(meshes[meshIndex]), part.Name, out part.Materials);
                var t = Json.List(node, "translation");
                if (t.Count >= 3)
                    part.Position = new Vector3(-Json.Num(t[0]), Json.Num(t[1]), Json.Num(t[2]));
                parts.Add(part);
            }
            error = parts.Count == 0 ? "no mesh nodes" : "";
            return parts.Count > 0;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static bool ReadFile(byte[] data, out Dictionary<string, object?>? root, out byte[]? bin, out string error)
    {
        root = null;
        bin = null;
        if (data.Length < 20 || BitConverter.ToUInt32(data, 0) != Magic)
        {
            error = "not a .glb file";
            return false;
        }
        string? json = null;
        var pos = 12;
        while (pos + 8 <= data.Length)
        {
            var len = (int)BitConverter.ToUInt32(data, pos);
            var type = BitConverter.ToUInt32(data, pos + 4);
            pos += 8;
            if (type == ChunkJson)
                json = Encoding.UTF8.GetString(data, pos, len);
            else if (type == ChunkBin)
            {
                bin = new byte[len];
                Buffer.BlockCopy(data, pos, bin, 0, len);
            }
            pos += len;
        }
        if (json == null || bin == null)
        {
            error = "missing JSON or BIN chunk";
            return false;
        }
        root = Json.Parse(json) as Dictionary<string, object?>;
        error = root == null ? "unreadable JSON" : "";
        return root != null;
    }

    static Mesh BuildMesh(Dictionary<string, object?> root, byte[] bin, Dictionary<string, object?>? meshDef, string name, out Material[] materials)
    {
        var accessors = Json.List(root, "accessors");
        var views = Json.List(root, "bufferViews");
        var matDefs = Json.List(root, "materials");
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int[]>();
        var mats = new List<Material>();
        foreach (var primObj in Json.List(meshDef, "primitives"))
        {
            var prim = Json.Obj(primObj);
            var attrs = Json.Obj(Json.Get(prim, "attributes"));
            var offset = vertices.Count;
            var pos3 = ReadVec(accessors, views, bin, Json.Int(attrs, "POSITION", -1), 3);
            var normalIndex = Json.Int(attrs, "NORMAL", -1);
            var uvIndex = Json.Int(attrs, "TEXCOORD_0", -1);
            var nrm3 = normalIndex >= 0 ? ReadVec(accessors, views, bin, normalIndex, 3) : null;
            var uv2 = uvIndex >= 0 ? ReadVec(accessors, views, bin, uvIndex, 2) : null;
            for (var i = 0; i < pos3.Length / 3; i++)
            {
                // glTF is right-handed, Unity left-handed: mirror X (and flip the winding below). The model's front (+Z) stays +Z.
                vertices.Add(new Vector3(-pos3[i * 3], pos3[i * 3 + 1], pos3[i * 3 + 2]));
                normals.Add(nrm3 != null ? new Vector3(-nrm3[i * 3], nrm3[i * 3 + 1], nrm3[i * 3 + 2]) : Vector3.up);
                uvs.Add(uv2 != null ? new Vector2(uv2[i * 2], 1f - uv2[i * 2 + 1]) : Vector2.zero);
            }
            var idx = ReadIndices(accessors, views, bin, Json.Int(prim, "indices", -1));
            for (var i = 0; i + 2 < idx.Length; i += 3)
            {
                var b = idx[i + 1];
                idx[i + 1] = idx[i + 2];
                idx[i + 2] = b;
            }
            for (var i = 0; i < idx.Length; i++)
                idx[i] += offset;
            tris.Add(idx);
            var matIndex = Json.Int(prim, "material", -1);
            mats.Add(MakeMaterial(matIndex >= 0 && matIndex < matDefs.Count ? Json.Obj(matDefs[matIndex]) : null));
        }

        var mesh = new Mesh { name = name };
        if (vertices.Count > 65000)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = tris.Count;
        for (var i = 0; i < tris.Count; i++)
            mesh.SetTriangles(tris[i], i);
        mesh.RecalculateBounds();
        materials = mats.ToArray();
        return mesh;
    }

    static float[] ReadVec(List<object?> accessors, List<object?> views, byte[] bin, int accessorIndex, int comps)
    {
        var acc = Json.Obj(accessors[accessorIndex]);
        var view = Json.Obj(views[Json.Int(acc, "bufferView", 0)]);
        var count = Json.Int(acc, "count", 0);
        var stride = Json.Int(view, "byteStride", 0);
        if (stride <= 0)
            stride = comps * 4;
        var start = Json.Int(view, "byteOffset", 0) + Json.Int(acc, "byteOffset", 0);
        var result = new float[count * comps];
        for (var i = 0; i < count; i++)
        for (var c = 0; c < comps; c++)
            result[i * comps + c] = BitConverter.ToSingle(bin, start + i * stride + c * 4);
        return result;
    }

    static int[] ReadIndices(List<object?> accessors, List<object?> views, byte[] bin, int accessorIndex)
    {
        var acc = Json.Obj(accessors[accessorIndex]);
        var view = Json.Obj(views[Json.Int(acc, "bufferView", 0)]);
        var count = Json.Int(acc, "count", 0);
        var kind = Json.Int(acc, "componentType", 5125);
        var start = Json.Int(view, "byteOffset", 0) + Json.Int(acc, "byteOffset", 0);
        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            switch (kind)
            {
                case 5121: result[i] = bin[start + i]; break;
                case 5123: result[i] = BitConverter.ToUInt16(bin, start + i * 2); break;
                default: result[i] = (int)BitConverter.ToUInt32(bin, start + i * 4); break;
            }
        }
        return result;
    }

    static Shader? _shader;

    /// <summary>The game's piece shader (Shader.Find does not see every shader in the player build, so look among the loaded ones).</summary>
    static Shader PieceShader()
    {
        if (_shader != null)
            return _shader;
        foreach (var name in new[] { "Custom/Piece", "Standard", "Sprites/Default" })
        {
            var found = Shader.Find(name);
            if (found == null)
                foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
                    if (s != null && s.name == name)
                    {
                        found = s;
                        break;
                    }
            if (found != null)
                return _shader = found;
        }
        throw new InvalidOperationException("no usable shader found");
    }

    /// <summary>Flat colors only. glTF colors are linear; the game draws in gamma space, so convert to match what Blender showed.</summary>
    static Material MakeMaterial(Dictionary<string, object?>? def)
    {
        var m = new Material(PieceShader()) { name = def != null ? Json.Str(def, "name", "Default") : "Default" };
        var pbr = Json.Obj(Json.Get(def, "pbrMetallicRoughness"));
        var f = Json.List(pbr, "baseColorFactor");
        var color = f.Count >= 3
            ? new Color(Mathf.LinearToGammaSpace(Json.Num(f[0])), Mathf.LinearToGammaSpace(Json.Num(f[1])), Mathf.LinearToGammaSpace(Json.Num(f[2])), 1f)
            : Color.white;
        m.color = color;
        if (m.HasProperty("_Glossiness"))
            m.SetFloat("_Glossiness", 1f - Mathf.Clamp01(Json.Float(pbr, "roughnessFactor", 1f)));
        if (m.HasProperty("_Metallic"))
            m.SetFloat("_Metallic", Mathf.Clamp01(Json.Float(pbr, "metallicFactor", 1f)));
        return m;
    }
}

/// <summary>A minimal JSON reader: objects, arrays, numbers, strings, true/false/null. Enough for glTF.</summary>
internal static class Json
{
    public static object? Parse(string text)
    {
        var i = 0;
        return Value(text, ref i);
    }

    public static Dictionary<string, object?>? Obj(object? o) => o as Dictionary<string, object?>;

    public static object? Get(Dictionary<string, object?>? o, string key) => o != null && o.TryGetValue(key, out var v) ? v : null;

    public static List<object?> List(Dictionary<string, object?>? o, string key) => Get(o, key) as List<object?> ?? new List<object?>();

    public static int Int(Dictionary<string, object?>? o, string key, int fallback) => Get(o, key) is double d ? (int)d : fallback;

    public static float Float(Dictionary<string, object?>? o, string key, float fallback) => Get(o, key) is double d ? (float)d : fallback;

    public static string Str(Dictionary<string, object?>? o, string key, string fallback) => Get(o, key) as string ?? fallback;

    public static float Num(object? o) => o is double d ? (float)d : 0f;

    static void Skip(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;
    }

    static object? Value(string s, ref int i)
    {
        Skip(s, ref i);
        if (i >= s.Length)
            throw new FormatException("unexpected end of JSON");
        switch (s[i])
        {
            case '{':
            {
                i++;
                var obj = new Dictionary<string, object?>();
                Skip(s, ref i);
                if (s[i] == '}')
                {
                    i++;
                    return obj;
                }
                while (true)
                {
                    Skip(s, ref i);
                    var key = String(s, ref i);
                    Skip(s, ref i);
                    if (s[i++] != ':')
                        throw new FormatException("expected ':' in JSON");
                    obj[key] = Value(s, ref i);
                    Skip(s, ref i);
                    var c = s[i++];
                    if (c == '}')
                        return obj;
                    if (c != ',')
                        throw new FormatException("expected ',' in JSON");
                }
            }
            case '[':
            {
                i++;
                var list = new List<object?>();
                Skip(s, ref i);
                if (s[i] == ']')
                {
                    i++;
                    return list;
                }
                while (true)
                {
                    list.Add(Value(s, ref i));
                    Skip(s, ref i);
                    var c = s[i++];
                    if (c == ']')
                        return list;
                    if (c != ',')
                        throw new FormatException("expected ',' in JSON array");
                }
            }
            case '"':
                return String(s, ref i);
            case 't':
                i += 4;
                return true;
            case 'f':
                i += 5;
                return false;
            case 'n':
                i += 4;
                return null;
            default:
            {
                var start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
                    i++;
                return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
            }
        }
    }

    static string String(string s, ref int i)
    {
        if (s[i] != '"')
            throw new FormatException("expected a string in JSON");
        i++;
        var sb = new StringBuilder();
        while (s[i] != '"')
        {
            if (s[i] == '\\')
            {
                i++;
                switch (s[i])
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16));
                        i += 4;
                        break;
                    default: sb.Append(s[i]); break;
                }
                i++;
            }
            else
                sb.Append(s[i++]);
        }
        i++;
        return sb.ToString();
    }
}
