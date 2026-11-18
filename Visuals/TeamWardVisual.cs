using System.Collections.Generic;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// The Team Ward's look and motion: an obsidian-dark body, rune glow and light in the team's glow color, the team logo and name floating above.
/// <para>
/// ON: the four big crystals stand out and show the team color, the six small diamonds float and orbit, the rune ring on the plinth glows.
/// OFF: the crystals fold in and sink, the diamonds are gone, the runes go dark. An enemy capture (server-written progress, see
/// <see cref="TeamWard.CaptureProgress"/>) slowly changes the rune ring to the capturing team's color, clockwise from 12 o'clock.
/// </para>
/// Display only. The team, power and capture come from the ward's ZDO, so every client sees the same thing.
/// All color changes go through per-renderer property blocks: the shared vanilla materials are never touched.
/// </summary>
public sealed class TeamWardVisual : MonoBehaviour
{
    const float RefreshSeconds = 0.5f;
    const float EmissionBoost = 4f;
    const float FoldSeconds = 1.6f;
    const float ClosedTilt = 30f;      // degrees the crystals lean further in when the ward is off
    const float ClosedSink = 0.34f;    // metres the crystals sink when the ward is off
    static readonly Color Obsidian = new(0.14f, 0.14f, 0.19f, 1f);
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    sealed class Part
    {
        public Transform T = null!;
        public MeshRenderer R = null!;
        public int StoneSlot = -1;
        public int GlowSlot = -1;
        public Vector3 BasePos;
        public Vector3 TiltAxis;
        public float InwardSign = 1f;
        public int Order;
    }

    readonly List<Part> _shards = new();
    readonly List<Part> _halos = new();
    readonly List<Part> _runes = new();
    readonly List<Part> _stone = new();   // the figure in the middle: the original stone (stone_body, stone_runes) or a figure_* animal
    GameObject? _figureHolder;
    string _figure = "";
    readonly List<Part> _channels = new();   // the light in the figure's cut-out channels, bottom to top (glow_*)
    readonly List<Part> _plugs = new();      // the stone pieces that fill those channels while the ward is off (plug_*), like the original guard stone
    float _flowPainted = -1f;

    PrivateArea? _area;
    Light? _light;
    SpriteRenderer? _icon;
    TextMesh? _label;
    string _applied = "~";
    string _glowHex = "none";
    float _next;
    float _on = 1f;            // 0..1 how far the ward is "on" right now (animated)
    float _onTarget = 1f;
    float _capture;            // displayed capture progress 0..1 (animated)
    float _captureTarget;
    string _captor = "";
    float _paintedCapture = -1f;
    float _paintedOn = -1f;
    TeamDefinition? _team;

    /// <summary>"team|glow|label|icon" for tests.</summary>
    public string Describe() =>
        $"{(_label != null ? _label.text : "")}|{_glowHex}|{(_icon != null && _icon.enabled ? "icon" : "none")}|{(_light != null && _light.enabled ? TeamIcons.ToHex(_light.color) : "none")}";

    /// <summary>"on|crystals open|halos shown|runes lit|capture" for tests.</summary>
    public string State()
    {
        var halos = 0;
        foreach (var h in _halos)
            if (h.R != null && h.R.enabled)
                halos++;
        var lit = 0;
        foreach (var r in _runes)
            if (r.Order < Mathf.CeilToInt(_capture * _runes.Count - 0.001f))
                lit++;
        return $"{(_onTarget > 0.5f ? "on" : "off")}|open {_on:0.00}|halos {halos}|captured {lit}/{_runes.Count}|capture {_capture:0.00}";
    }

    /// <summary>True when the Blender model is on this ward (otherwise the plain guard stone look is kept).</summary>
    public bool HasModel => _shards.Count > 0;

    static Part MakePart(MeshRenderer r)
    {
        var part = new Part { T = r.transform, R = r, BasePos = r.transform.localPosition };
        var mats = r.sharedMaterials;
        for (var i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null)
                continue;
            if (mats[i].name.Contains("OdenGlow"))
                part.GlowSlot = i;
            else if (mats[i].name.Contains("Oden_mat"))
                part.StoneSlot = i;
        }
        return part;
    }

    /// <summary>Which figure stands on this ward right now (see <see cref="WardFigures"/>), for tests.</summary>
    public string Figure => _figure;

    /// <summary>Put the named figure in the middle of the ward (the old one is removed). Its parts take part in the power dimming of the glow.</summary>
    void ApplyFigure(string id)
    {
        id = WardFigures.Normalize(id);
        if (id == _figure && _figureHolder != null)
            return;
        if (_figureHolder != null)
            Destroy(_figureHolder);
        _stone.Clear();
        _channels.Clear();
        _plugs.Clear();
        _figure = id;
        _figureHolder = WardFigures.Build(transform, id);
        if (_figureHolder == null)
            return;
        foreach (var r in _figureHolder.GetComponentsInChildren<MeshRenderer>(true))
        {
            var part = MakePart(r);
            var partName = r.gameObject.name;
            if (partName.StartsWith("glow_"))
                _channels.Add(part);     // the light in the cut-out channels
            else if (partName.StartsWith("plug_"))
                _plugs.Add(part);        // stone pieces that fill the channels while the ward is off
            else
                _stone.Add(part);
        }
        // the channels are numbered from the bottom of the body to the top: the flow runs that way
        _channels.Sort((a, b) => string.CompareOrdinal(a.R.gameObject.name, b.R.gameObject.name));
        for (var i = 0; i < _channels.Count; i++)
            _channels[i].Order = i;
        _flowPainted = -1f;
    }

    /// <summary>
    /// The glowing channels carved into a figure, like the runes on the guard stone: a pulse of light runs up them, slowly, and they go dark
    /// with the ward. Team glow color; neutral wards keep the vanilla look.
    /// </summary>
    void FlowChannels()
    {
        if (_channels.Count == 0 || _team == null)
            return;
        var t = Time.time;
        var glow = TeamIcons.ParseColor(_team.EffectiveGlowColor()) * (EmissionBoost * 1.5f);   // thin lines need more light than the stone's wide glyphs
        var n = _channels.Count;
        // repaint at ~20 Hz, plenty for a slow wave
        if (t - _flowPainted < 0.05f)
            return;
        _flowPainted = t;
        var block = new MaterialPropertyBlock();
        foreach (var c in _channels)
        {
            if (c.GlowSlot < 0)
                continue;
            var wave = 0.5f + 0.5f * Mathf.Sin(Mathf.PI * 2f * (t * 0.35f - (float)c.Order / n));
            var level = Mathf.Lerp(0.12f, 0.70f + 0.60f * wave, _on);
            block.Clear();
            block.SetColor(EmissionId, glow * level);
            c.R.SetPropertyBlock(block, c.GlowSlot);
        }
    }

    /// <summary>For tests: what the first, middle and last flow channel look like to the game (mesh, materials, bounds and the emission set on them).</summary>
    public string DescribeChannels()
    {
        if (_channels.Count == 0)
            return "no channels";
        var shown = 0;
        foreach (var p in _plugs)
            if (p.R != null && p.R.enabled)
                shown++;
        var list = new List<string> { $"plugs {_plugs.Count} shown {shown} (on {_on:0.00})" };
        foreach (var i in new[] { 0, _channels.Count / 2, _channels.Count - 1 })
        {
            var c = _channels[i];
            var mf = c.R.GetComponent<MeshFilter>();
            var block = new MaterialPropertyBlock();
            c.R.GetPropertyBlock(block, Mathf.Max(0, c.GlowSlot));
            var mats = new List<string>();
            foreach (var m in c.R.sharedMaterials)
                mats.Add(m != null ? m.name + "/" + m.shader.name : "null");
            list.Add($"{c.R.gameObject.name} enabled {c.R.enabled} active {c.R.gameObject.activeInHierarchy} verts {mf.sharedMesh.vertexCount} subs {mf.sharedMesh.subMeshCount} bounds {c.R.bounds.center:F2} {c.R.bounds.size:F2} glowSlot {c.GlowSlot} mats {string.Join("+", mats)} emission {block.GetColor(EmissionId)}");
        }
        return string.Join(" || ", list);
    }

    /// <summary>How many flow channels the figure has (0 for the original stone and the borrowed statues), for tests.</summary>
    public int ChannelCount => _channels.Count;

    static bool IsFigureGlow(string partName) => partName == "stone_runes" || partName.StartsWith("figure_");

    void Awake()
    {
        _area = GetComponent<PrivateArea>();
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            var n = r.gameObject.name;
            var part = MakePart(r);
            if (n.StartsWith("shard_"))
                _shards.Add(part);
            else if (n.StartsWith("halo_"))
                _halos.Add(part);
            else if (n.StartsWith("rune_"))
                _runes.Add(part);
        }
        foreach (var s in _shards)
        {
            var outward = new Vector3(s.BasePos.x, 0f, s.BasePos.z).normalized;
            s.TiltAxis = Vector3.Cross(Vector3.up, outward);
            // which way is "in"? turn the up vector a little about the axis and see which side it falls on
            s.InwardSign = Vector3.Dot(Quaternion.AngleAxis(10f, s.TiltAxis) * Vector3.up, outward) < 0f ? 1f : -1f;
        }
        // the rune ring sweeps clockwise from 12 o'clock (+Z); order each segment by where it sits
        foreach (var r in _runes)
        {
            var b = r.R.GetComponent<MeshFilter>().sharedMesh.bounds.center;
            var angle = Mathf.Atan2(b.x, b.z) * Mathf.Rad2Deg;
            r.Order = Mathf.RoundToInt(((angle % 360f) + 360f) % 360f / 30f) % Mathf.Max(1, _runes.Count);
        }
    }

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
        Animate(Time.deltaTime);
    }

    void Animate(float dt)
    {
        _on = Mathf.MoveTowards(_on, _onTarget, dt / FoldSeconds);
        _capture = Mathf.MoveTowards(_capture, _captureTarget, dt * 0.25f);   // slow, so a jump in progress still reads as a sweep
        if (_light != null)
        {
            _light.enabled = TeamsConfig.ShowGlow && _team != null && _on > 0.02f;
            _light.intensity = TeamsConfig.GlowIntensity * 1.6f * _on;
        }

        var t = Time.time;
        foreach (var s in _shards)
        {
            var close = 1f - _on;
            s.T.localRotation = Quaternion.AngleAxis(s.InwardSign * ClosedTilt * close, s.TiltAxis);
            s.T.localPosition = s.BasePos + Vector3.down * (ClosedSink * close);
        }
        for (var i = 0; i < _halos.Count; i++)
        {
            var h = _halos[i];
            var shown = _on > 0.02f;
            if (h.R.enabled != shown)
                h.R.enabled = shown;
            if (!shown)
                continue;
            var orbit = Quaternion.AngleAxis(t * 16f, Vector3.up) * h.BasePos;
            orbit.y = h.BasePos.y + Mathf.Sin(t * 1.7f + i * 1.1f) * 0.07f * _on - (1f - _on) * 0.9f;
            h.T.localPosition = orbit;
            h.T.localRotation = Quaternion.AngleAxis(t * 55f + i * 40f, Vector3.up);
            h.T.localScale = Vector3.one * _on;
        }

        if (Mathf.Abs(_on - _paintedOn) > 0.004f || Mathf.Abs(_capture - _paintedCapture) > 0.002f)
            PaintLive();
        // off: the stone plugs sit in the channels and the light is hidden behind them; on: they are taken out and the light shows
        var plugged = _on < 0.5f;
        foreach (var p in _plugs)
            if (p.R != null && p.R.enabled != plugged)
                p.R.enabled = plugged;
        FlowChannels();
    }

    /// <summary>
    /// The game's own glow, pulse and flare effects of a ward only run while the ward's saved "enabled" flag is set (our patch makes IsEnabled
    /// say yes, but the effect object follows the flag). Set it, on the owner, so a Team Ward pulses light like the original.
    /// </summary>
    void EnsureEnabledFlag()
    {
        var view = _area != null ? _area.GetComponent<ZNetView>() : null;
        if (view == null || !view.IsValid() || !view.IsOwner())
            return;
        var zdo = view.GetZDO();
        if (zdo != null && !zdo.GetBool("enabled", false))
            zdo.Set("enabled", true);
    }

    /// <summary>The vanilla light effects (glow, pulse, flare, sparks) in the team's glow color, each keeping its own opacity.</summary>
    void TintEffects(Color glow)
    {
        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            var mat = r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "";
            if (mat != "glow" && mat != "glow_pulse" && mat != "light_glow" && mat != "gnista")
                continue;
            var main = ps.main;
            var old = main.startColor.color;
            main.startColor = new Color(glow.r, glow.g, glow.b, old.a);
        }
    }

    void Apply(bool force)
    {
        if (_area == null)
            return;
        EnsureEnabledFlag();
        var teamId = TeamWard.TeamOf(_area) ?? "";
        var team = teamId.Length > 0 ? TeamsAPI.GetTeam(teamId) : null;
        _onTarget = TeamWard.IsPowered(_area) ? 1f : 0f;
        _captureTarget = TeamWard.CaptureProgress(_area);
        _captor = TeamWard.Captor(_area) ?? "";
        if (_captor.Length == 0)
            _captureTarget = 0f;
        if (_applied == "~")
        {
            // first look at this ward: show its state, do not animate into it
            _on = _onTarget;
            _capture = _captureTarget;
        }
        var captorTeam = _captor.Length > 0 ? TeamsAPI.GetTeam(_captor) : null;
        var key = (team != null ? team.Id + team.Color + team.GlowColor + team.Icon + team.Logo + team.DisplayName + team.Figure : "") + "|" + (captorTeam != null ? captorTeam.Id + captorTeam.GlowColor + captorTeam.Color : "");
        if (!force && key == _applied)
            return;
        _applied = key;
        _team = team;

        if (HasModel)
            ApplyFigure(team?.Figure ?? WardFigures.Default);
        PaintBody(team);
        _paintedOn = -1f;   // repaint the animated parts with the new team
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
        TintEffects(glow);

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
        _light.intensity = TeamsConfig.GlowIntensity * 1.6f * _on;
        _light.enabled = TeamsConfig.ShowGlow && _on > 0.02f;

        if (_icon == null)
        {
            var go = new GameObject("DrakesServerTeams_wardIcon");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 3.5f, 0f);
            go.transform.localScale = Vector3.one * 0.8f;
            _icon = go.AddComponent<SpriteRenderer>();
            _icon.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }
        _icon.sprite = TeamLogos.GetSprite(team, out var tinted);
        _icon.color = tinted ? color : Color.white;
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

    /// <summary>The stone and the (always static) vanilla pieces: obsidian body, team rune glow. Neutral wards keep the vanilla glow.</summary>
    void PaintBody(TeamDefinition? team)
    {
        var block = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is not MeshRenderer)
                continue;
            var n = r.gameObject.name;
            if (n.StartsWith("shard_") || n.StartsWith("halo_") || n.StartsWith("rune_"))
                continue; // the animated parts are painted by PaintLive
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
                    block.SetColor(EmissionId, TeamIcons.ParseColor(team.EffectiveGlowColor()) * EmissionBoost * GlowLevel(n));
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

    float GlowLevel(string partName) => IsFigureGlow(partName) ? 0.12f + 0.88f * _on : 1f;

    /// <summary>The moving parts: team-colored crystals, rune ring brightness and the capture sweep. Runs when power or capture changes.</summary>
    void PaintLive()
    {
        _paintedOn = _on;
        _paintedCapture = _capture;
        var block = new MaterialPropertyBlock();
        var team = _team;
        var glow = team != null ? TeamIcons.ParseColor(team.EffectiveGlowColor()) : new Color(0.55f, 0.75f, 1f);
        var color = team != null ? TeamIcons.ParseColor(team.Color) : new Color(0.5f, 0.55f, 0.65f);
        var captorTeam = _captor.Length > 0 ? TeamsAPI.GetTeam(_captor) : null;
        var captorGlow = captorTeam != null ? TeamIcons.ParseColor(captorTeam.EffectiveGlowColor()) : glow;

        void Stone(Part p, Color c)
        {
            if (p.StoneSlot < 0)
                return;
            block.Clear();
            block.SetColor(ColorId, c);
            p.R.SetPropertyBlock(block, p.StoneSlot);
        }

        void Glow(Part p, Color emission)
        {
            if (p.GlowSlot < 0)
                return;
            block.Clear();
            block.SetColor(EmissionId, emission);
            p.R.SetPropertyBlock(block, p.GlowSlot);
        }

        // the stone's own rune glow dims with power (its team tint is in PaintBody)
        foreach (var s in _stone)
            if (IsFigureGlow(s.R.gameObject.name) && team != null)
                Glow(s, glow * EmissionBoost * (0.12f + 0.88f * _on));

        foreach (var s in _shards)
        {
            Stone(s, color * Mathf.Lerp(0.30f, 0.62f, _on));
            Glow(s, glow * EmissionBoost * (0.15f + 0.85f * _on));
        }
        foreach (var h in _halos)
        {
            Stone(h, color * 0.8f);
            Glow(h, glow * EmissionBoost);
        }
        var count = Mathf.Max(1, _runes.Count);
        foreach (var r in _runes)
        {
            var baseLevel = 0.05f + 0.85f * _on;
            var fill = Mathf.Clamp01(_capture * count - r.Order);
            var emission = Color.Lerp(glow * baseLevel, captorGlow * Mathf.Max(baseLevel, 0.5f + 0.5f * _on), fill) * EmissionBoost;
            Glow(r, emission);
            Stone(r, Obsidian);
        }
    }
}
