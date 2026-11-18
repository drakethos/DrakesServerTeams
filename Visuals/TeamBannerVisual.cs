using System.Collections.Generic;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// The cloth picture for a team's banner or flag: the team color with a border and the team logo placed where the model's
/// cloth UVs put the "logo area". One texture per team and cloth shape, cached.
/// </summary>
internal static class BannerTextures
{
    /// <summary>Tall cloth (hanging, standing and carried banners, 1:2) or wide cloth (the flag, 3:2).</summary>
    public enum Shape { Tall, Wide }

    static readonly Dictionary<string, Texture2D> Cache = new();

    public static void ClearCache()
    {
        foreach (var t in Cache.Values)
            if (t != null)
                Object.Destroy(t);
        Cache.Clear();
    }

    public static Texture2D Get(TeamDefinition? team, Shape shape)
    {
        var key = (team == null ? "-" : team.Id + team.Color + team.Icon + team.Logo) + shape;
        if (Cache.TryGetValue(key, out var cached) && cached != null)
            return cached;
        var tex = Make(team, shape);
        Cache[key] = tex;
        return tex;
    }

    static Texture2D Make(TeamDefinition? team, Shape shape)
    {
        var tall = shape == Shape.Tall;
        var w = tall ? 256 : 384;
        var h = tall ? 512 : 256;
        var cloth = team != null ? TeamIcons.ParseColor(team.Color) * 0.92f : new Color(0.55f, 0.55f, 0.55f);
        cloth.a = 1f;
        var edge = new Color(cloth.r * 0.65f, cloth.g * 0.65f, cloth.b * 0.65f, 1f);
        var band = Mathf.Max(4, Mathf.Min(w, h) / 22);
        var px = new Color32[w * h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var onEdge = x < band || x >= w - band || y >= h - band;
            px[y * w + x] = onEdge ? edge : cloth;
        }

        if (team != null)
        {
            var logoSize = tall ? (int)(w * 0.74f) : (int)(h * 0.70f);
            var cx = tall ? w / 2 : (int)(w * 0.46f);
            var cy = tall ? (int)(h * 0.66f) : h / 2;
            var logo = new Color32[logoSize * logoSize];
            if (!LogoSpec.TryParse(team.Logo, out var spec, out _))
                spec = LogoSpec.Starter(LogoShapes.IsBase(team.Icon) ? team.Icon : "shield", team.Color);
            LogoRenderer.RenderInto(spec, logoSize, logo);
            for (var ly = 0; ly < logoSize; ly++)
            for (var lx = 0; lx < logoSize; lx++)
            {
                var src = logo[ly * logoSize + lx];
                if (src.a == 0)
                    continue;
                var dx = cx - logoSize / 2 + lx;
                var dy = cy - logoSize / 2 + ly;
                if (dx < 0 || dy < 0 || dx >= w || dy >= h)
                    continue;
                var dst = px[dy * w + dx];
                var a = src.a / 255f;
                px[dy * w + dx] = new Color32((byte)Mathf.Lerp(dst.r, src.r, a), (byte)Mathf.Lerp(dst.g, src.g, a), (byte)Mathf.Lerp(dst.b, src.b, a), 255);
            }
        }

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "TeamBannerCloth" };
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}

/// <summary>
/// Paints a banner's cloth with its team's picture. The team comes from the server-signed stamp on the piece, so every client
/// shows the same thing. Display only: the shared cloth material is never changed, only a per-renderer property block.
/// </summary>
public sealed class TeamBannerVisual : MonoBehaviour
{
    const float RefreshSeconds = 0.5f;
    static readonly int MainTex = Shader.PropertyToID("_MainTex");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    internal BannerTextures.Shape ClothShape;
    internal string DisplayName = "Banner";

    readonly List<(Renderer renderer, int slot)> _cloth = new();
    ZNetView? _nview;
    string _applied = "~";
    string _team = "";
    float _next;

    /// <summary>"team id or empty" for tests.</summary>
    public string Describe() => _team;

    void Awake()
    {
        _nview = GetComponentInParent<ZNetView>();
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            for (var i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].name == "TeamCloth")
                    _cloth.Add((r, i));
        }
    }

    void Start() => Apply(true);

    void Update()
    {
        if (Time.unscaledTime < _next)
            return;
        _next = Time.unscaledTime + RefreshSeconds;
        Apply(false);
    }

    void Apply(bool force)
    {
        var zdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
        var teamId = PieceTeams.ReadTeam(zdo) ?? "";
        var team = teamId.Length > 0 ? TeamsAPI.GetTeam(teamId) : null;
        var key = team != null ? team.Id + team.Color + team.Icon + team.Logo : "";
        if (!force && key == _applied)
            return;
        _applied = key;
        _team = team?.Id ?? "";
        var tex = BannerTextures.Get(team, ClothShape);
        var block = new MaterialPropertyBlock();
        foreach (var (renderer, slot) in _cloth)
        {
            if (renderer == null)
                continue;
            block.Clear();
            block.SetTexture(MainTex, tex);
            block.SetColor(ColorId, Color.white);
            renderer.SetPropertyBlock(block, slot);
        }
    }

}

/// <summary>
/// The carried banner's cloth: painted with the team of the player holding it (neutral grey when nobody does, e.g. on the ground).
/// </summary>
public sealed class CarriedBannerVisual : MonoBehaviour
{
    const float RefreshSeconds = 0.5f;
    static readonly int MainTex = Shader.PropertyToID("_MainTex");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    internal BannerTextures.Shape ClothShape;

    readonly List<(Renderer renderer, int slot)> _cloth = new();
    string _applied = "~";
    string _team = "";
    float _next;

    /// <summary>Team id of the carrier, or empty.</summary>
    public string Describe() => _team;

    void Awake()
    {
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            for (var i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].name == "TeamCloth")
                    _cloth.Add((r, i));
        }
    }

    void Start() => Apply(true);

    void Update()
    {
        if (Time.unscaledTime < _next)
            return;
        _next = Time.unscaledTime + RefreshSeconds;
        Apply(false);
    }

    void Apply(bool force)
    {
        var carrier = GetComponentInParent<Player>();
        var teamId = carrier != null ? TeamsAPI.GetTeamId(carrier) ?? "" : "";
        var team = teamId.Length > 0 ? TeamsAPI.GetTeam(teamId) : null;
        var key = team != null ? team.Id + team.Color + team.Icon + team.Logo : "";
        if (!force && key == _applied)
            return;
        _applied = key;
        _team = team?.Id ?? "";
        var tex = BannerTextures.Get(team, ClothShape);
        var block = new MaterialPropertyBlock();
        foreach (var (renderer, slot) in _cloth)
        {
            if (renderer == null)
                continue;
            block.Clear();
            block.SetTexture(MainTex, tex);
            block.SetColor(ColorId, Color.white);
            renderer.SetPropertyBlock(block, slot);
        }
    }
}
