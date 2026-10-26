using TMPro;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// A grave carries the team of the player who died: a light, the name text and a floating icon in the team color, and a
/// line in the hover text. The dying player's own client writes the team on the grave (display only, like the team on a
/// player); everyone reads it.
/// </summary>
public sealed class TeamGrave : MonoBehaviour
{
    public static readonly int GraveTeamKey = "DrakesServerTeams_graveTeam".GetStableHashCode();
    const float RefreshSeconds = 0.5f;

    TombStone? _tomb;
    Light? _light;
    SpriteRenderer? _icon;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    // The grave model glows red on its own: an emissive material, a red particle effect and gold sparks. Team graves recolor all three.
    const float BandIntensity = 4f;

    string _applied = "";
    Renderer? _band;
    MaterialPropertyBlock? _bandBlock;
    ParticleSystem[]? _particles;
    Color[]? _particleOriginals;
    float _next;

    public string TeamId { get; private set; } = "";

    /// <summary>"team|name color|light color|icon shown" for tests.</summary>
    public string Describe()
    {
        var worldText = _tomb != null ? GameFields.WorldText(_tomb) : null;
        var text = worldText != null ? TeamIcons.ToHex(worldText.color) : "-";
        return $"{TeamId}|{text}|{(_light != null && _light.enabled ? TeamIcons.ToHex(_light.color) : "none")}|{(_icon != null && _icon.enabled ? "icon" : "none")}|{BandColor()}";
    }

    /// <summary>The color of the emissive band at the base of the grave: "#RRGGBB" when a team color is applied, "none" otherwise.</summary>
    string BandColor()
    {
        if (_applied.Length == 0 || _band == null || _bandBlock == null)
            return "none";
        _band.GetPropertyBlock(_bandBlock);
        var emission = _bandBlock.GetColor(EmissionId);
        return emission.maxColorComponent <= 0f ? "none" : TeamIcons.ToHex(emission / BandIntensity);
    }

    void FindParts()
    {
        if (_band != null || _tomb == null)
            return;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is MeshRenderer && r.sharedMaterial != null && r.sharedMaterial.HasProperty(EmissionId)
                && r.sharedMaterial.GetColor(EmissionId).maxColorComponent > 1f)
            {
                _band = r;
                break;
            }
        }
        _bandBlock ??= new MaterialPropertyBlock();
        _particles = GetComponentsInChildren<ParticleSystem>(true);
        _particleOriginals = new Color[_particles.Length];
        for (var i = 0; i < _particles.Length; i++)
            _particleOriginals[i] = _particles[i].main.startColor.color;
    }

    void ApplyBand(Color glow)
    {
        FindParts();
        if (_band != null && _bandBlock != null)
        {
            _band.GetPropertyBlock(_bandBlock);
            _bandBlock.SetColor(EmissionId, glow * BandIntensity);
            _band.SetPropertyBlock(_bandBlock);
        }
        if (_particles == null || _particleOriginals == null)
            return;
        for (var i = 0; i < _particles.Length; i++)
        {
            var main = _particles[i].main;
            main.startColor = new Color(glow.r, glow.g, glow.b, _particleOriginals[i].a);
        }
    }

    void RestoreBand()
    {
        if (_band != null)
            _band.SetPropertyBlock(null);
        if (_particles == null || _particleOriginals == null)
            return;
        for (var i = 0; i < _particles.Length; i++)
        {
            var main = _particles[i].main;
            main.startColor = _particleOriginals[i];
        }
    }

    public static string ReadTeam(TombStone tomb) =>
        GameFields.View(tomb) is { } view && view.IsValid() ? view.GetZDO().GetString(GraveTeamKey, "") : "";

    void Awake() => _tomb = GetComponent<TombStone>();

    void Update()
    {
        if (Time.unscaledTime >= _next)
        {
            _next = Time.unscaledTime + RefreshSeconds;
            Refresh();
        }
        if (_icon != null && _icon.enabled && Camera.main != null)
            _icon.transform.rotation = Camera.main.transform.rotation;
    }

    void Refresh()
    {
        if (_tomb == null)
            return;
        TeamId = TeamsConfig.ShowGraveMarker ? ReadTeam(_tomb) : "";
        var team = TeamId.Length > 0 ? TeamsAPI.GetTeam(TeamId) : null;
        if (team == null)
        {
            if (_applied.Length > 0)
                Hide();
            return;
        }
        if (_applied == team.Id + team.Color + team.Icon + team.GlowColor)
            return;
        _applied = team.Id + team.Color + team.Icon + team.GlowColor;
        var color = TeamIcons.ParseColor(team.Color);

        var worldText = GameFields.WorldText(_tomb);
        if (worldText != null)
            worldText.color = color;

        if (_light == null)
        {
            var go = new GameObject("DrakesServerTeams_graveLight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.shadows = LightShadows.None;
            _light.range = 6f;
            _light.intensity = TeamsConfig.GlowIntensity * 1.4f;
        }
        var glow = TeamIcons.ParseColor(team.EffectiveGlowColor());
        _light.color = glow;
        _light.enabled = true;
        ApplyBand(glow);

        if (_icon == null)
        {
            var go = new GameObject("DrakesServerTeams_graveIcon");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            go.transform.localScale = Vector3.one * 0.9f;
            _icon = go.AddComponent<SpriteRenderer>();
            _icon.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }
        _icon.sprite = TeamIcons.Get(team.Icon);
        _icon.color = TeamIcons.IsTinted(team.Icon) ? color : Color.white;
        _icon.enabled = true;
    }

    void Hide()
    {
        _applied = "";
        if (_light != null)
            _light.enabled = false;
        if (_icon != null)
            _icon.enabled = false;
        var worldText = _tomb != null ? GameFields.WorldText(_tomb) : null;
        if (worldText != null)
            worldText.color = Color.white;
        RestoreBand();
    }
}
