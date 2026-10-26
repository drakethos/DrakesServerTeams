using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// A soft light in the team color around a player. Added to every Player; it stays off (and costs nothing) while
/// the player has no team or the server turned glow off. The team comes from <see cref="TeamsAPI.GetTeamId(Player)"/>
/// (what the player publishes, corrected by the server).
/// </summary>
public sealed class TeamGlow : MonoBehaviour
{
    const float RefreshSeconds = 0.5f;

    Player? _player;
    Light? _light;
    float _next;

    /// <summary>The glow's current color as "#RRGGBB", or "none" when it is off (for tests).</summary>
    public string Describe() => _light != null && _light.enabled ? TeamIcons.ToHex(_light.color) : "none";

    void Awake() => _player = GetComponent<Player>();

    void Update()
    {
        if (Time.unscaledTime < _next)
            return;
        _next = Time.unscaledTime + RefreshSeconds;
        Refresh();
    }

    void Refresh()
    {
        var team = TeamsConfig.ShowGlow ? TeamsAPI.GetTeam(TeamsAPI.GetTeamId(_player)) : null;
        if (team == null)
        {
            if (_light != null)
                _light.enabled = false;
            return;
        }
        if (_light == null)
        {
            var go = new GameObject("DrakesServerTeams_glow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1f, 0f);
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.shadows = LightShadows.None;
        }
        _light.color = TeamIcons.ParseColor(team.EffectiveGlowColor());
        _light.range = TeamsConfig.GlowRange;
        _light.intensity = TeamsConfig.GlowIntensity;
        _light.enabled = true;
    }
}
