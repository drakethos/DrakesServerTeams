using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DrakesServerTeams;

/// <summary>Every player gets a <see cref="TeamGlow"/>; it does nothing until the player has a team.</summary>
[HarmonyPatch(typeof(Player), "Awake")]
static class PlayerGlowPatch
{
    static void Postfix(Player __instance)
    {
        if (!Application.isBatchMode && __instance.GetComponent<TeamGlow>() == null)
            __instance.gameObject.AddComponent<TeamGlow>();
    }
}

/// <summary>
/// Nameplate above other players: name in the team color and the team icon above it. Runs after the game has updated
/// the plates; a plate without a team (or with the setting off) is put back the way the game draws it.
/// </summary>
[HarmonyPatch(typeof(EnemyHud), "LateUpdate")]
static class NameplatePatch
{
    const string IconName = "DrakesServerTeams_icon";

    static void Postfix(EnemyHud __instance)
    {
        GameFields.EachPlate(__instance, (character, text) =>
        {
            if (character is Player player)
                Apply(text, player);
        });
    }

    internal static string Apply(TMPro.TMP_Text name, Player player)
    {
        var team = TeamsAPI.GetTeam(TeamsAPI.GetTeamId(player));
        var iconTf = name.transform.Find(IconName);
        if (team == null)
        {
            if (iconTf != null)
            {
                Object.Destroy(iconTf.gameObject);
                name.color = Color.white;
            }
            return "none";
        }

        var color = TeamIcons.ParseColor(team.Color);
        if (TeamsConfig.ShowNameColor)
            name.color = color;

        if (!TeamsConfig.ShowIcon)
        {
            if (iconTf != null)
                iconTf.gameObject.SetActive(false);
            return TeamIcons.ToHex(name.color) + "|no icon";
        }
        Image image;
        if (iconTf == null)
        {
            var go = new GameObject(IconName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(name.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 4f);
            rt.sizeDelta = new Vector2(26f, 26f);
            image = go.GetComponent<Image>();
            image.raycastTarget = false;
        }
        else
        {
            iconTf.gameObject.SetActive(true);
            image = iconTf.GetComponent<Image>();
        }
        image.sprite = TeamIcons.Get(team.Icon);
        image.color = TeamIcons.IsTinted(team.Icon) ? color : Color.white;
        return TeamIcons.ToHex(name.color) + "|" + TeamIcons.Normalize(team.Icon);
    }
}

/// <summary>The player who dies writes their team on the grave they leave.</summary>
[HarmonyPatch(typeof(TombStone), nameof(TombStone.Setup))]
static class GraveSetupPatch
{
    static void Postfix(TombStone __instance, long ownerUID)
    {
        var view = GameFields.View(__instance);
        if (view == null || !view.IsValid())
            return;
        view.GetZDO().Set(TeamGrave.GraveTeamKey, TeamsAPI.GetTeamId(ownerUID) ?? "");
    }
}

[HarmonyPatch(typeof(TombStone), "Awake")]
static class GraveAwakePatch
{
    static void Postfix(TombStone __instance)
    {
        if (!Application.isBatchMode && __instance.GetComponent<TeamGrave>() == null)
            __instance.gameObject.AddComponent<TeamGrave>();
    }
}

/// <summary>"Grave of Alice" gets the team name under it.</summary>
[HarmonyPatch(typeof(TombStone), nameof(TombStone.GetHoverText))]
static class GraveHoverPatch
{
    static void Postfix(TombStone __instance, ref string __result)
    {
        if (!TeamsConfig.ShowGraveMarker)
            return;
        var team = TeamsAPI.GetTeam(TeamGrave.ReadTeam(__instance));
        if (team != null)
            __result += $"\n<color={team.Color}>{team.DisplayName}</color>";
    }
}
