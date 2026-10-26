using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// The Team Ward's look: an obsidian-dark body, rune glow and light in the team's glow color, the team icon and name floating above it.
/// Display only. The team comes from the server-signed stamp on the ward, so every client sees the same thing.
/// All changes go through per-renderer property blocks: the shared vanilla materials are never touched.
/// </summary>
public sealed class TeamWardVisual : MonoBehaviour
{
    const float RefreshSeconds = 0.5f;
    const float EmissionBoost = 4f;
    static readonly Color Obsidian = new(0.14f, 0.14f, 0.19f, 1f);
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    PrivateArea? _area;
    Light? _light;
    SpriteRenderer? _icon;
    TextMesh? _label;
    string _applied = "~";
    string _glowHex = "none";
    float _next;

    /// <summary>"team|glow|label|icon" for tests.</summary>
    public string Describe() =>
        $"{(_label != null ? _label.text : "")}|{_glowHex}|{(_icon != null && _icon.enabled ? "icon" : "none")}|{(_light != null && _light.enabled ? TeamIcons.ToHex(_light.color) : "none")}";

    void Awake() => _area = GetComponent<PrivateArea>();

    void Start() => Apply(force: true);

    void Update()
    {
        if (Time.unscaledTime >= _next)
        {
            _next = Time.unscaledTime + RefreshSeconds;
            Apply(force: false);
        }
        if (Camera.main != null)
        {
            var rot = Camera.main.transform.rotation;
            if (_icon != null)
                _icon.transform.rotation = rot;
            if (_label != null)
                _label.transform.rotation = rot;
        }
    }

    void Apply(bool force)
    {
        if (_area == null)
            return;
        var teamId = TeamWard.TeamOf(_area) ?? "";
        var team = teamId.Length > 0 ? TeamsAPI.GetTeam(teamId) : null;
        var key = team != null ? team.Id + team.Color + team.GlowColor + team.Icon + team.DisplayName : "";
        if (!force && key == _applied)
            return;
        _applied = key;

        PaintBody(team);
        if (team == null)
        {
            if (_light != null) _light.enabled = false;
            if (_icon != null) _icon.enabled = false;
            if (_label != null) _label.text = "";
            _glowHex = "none";
            return;
        }

        var color = TeamIcons.ParseColor(team.Color);
        var glow = TeamIcons.ParseColor(team.EffectiveGlowColor());
        _glowHex = TeamIcons.ToHex(glow);

        if (_light == null)
        {
            var go = new GameObject("DrakesServerTeams_wardLight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.shadows = LightShadows.None;
            _light.range = 9f;
        }
        _light.color = glow;
        _light.intensity = TeamsConfig.GlowIntensity * 1.6f;
        _light.enabled = TeamsConfig.ShowGlow;

        if (_icon == null)
        {
            var go = new GameObject("DrakesServerTeams_wardIcon");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 3.5f, 0f);
            go.transform.localScale = Vector3.one * 0.8f;
            _icon = go.AddComponent<SpriteRenderer>();
            _icon.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }
        _icon.sprite = TeamIcons.Get(team.Icon);
        _icon.color = TeamIcons.IsTinted(team.Icon) ? color : Color.white;
        _icon.enabled = TeamsConfig.ShowIcon;

        if (_label == null)
        {
            var go = new GameObject("DrakesServerTeams_wardLabel");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 2.95f, 0f);
            _label = go.AddComponent<TextMesh>();
            _label.font = Font.CreateDynamicFontFromOSFont("Arial", 48);
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.characterSize = 0.08f;
            _label.fontSize = 64;
            _label.fontStyle = FontStyle.Bold;
            var r = go.GetComponent<MeshRenderer>();
            if (r != null && _label.font != null)
                r.sharedMaterial = _label.font.material;
        }
        _label.text = team.DisplayName;
        _label.color = TeamsConfig.ShowNameColor ? color : Color.white;
    }

    /// <summary>Obsidian body and team-colored rune glow (neutral wards keep the vanilla glow).</summary>
    void PaintBody(TeamDefinition? team)
    {
        var block = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is not MeshRenderer)
                continue;
            var mats = r.sharedMaterials;
            for (var i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null)
                    continue;
                var name = m.name;
                block.Clear();
                if (name.Contains("OdenGlow"))
                {
                    if (team == null || !m.HasProperty(EmissionId))
                    {
                        r.SetPropertyBlock(null, i);
                        continue;
                    }
                    block.SetColor(EmissionId, TeamIcons.ParseColor(team.EffectiveGlowColor()) * EmissionBoost);
                    r.SetPropertyBlock(block, i);
                }
                else if (name.Contains("Oden_mat") && m.HasProperty(ColorId))
                {
                    block.SetColor(ColorId, Obsidian);
                    r.SetPropertyBlock(block, i);
                }
            }
        }
    }
}
