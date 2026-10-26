using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using TMPro;

namespace DrakesServerTeams;

/// <summary>Private game members the visuals need. The build compiles against publicized assemblies, the real game does not allow direct access.</summary>
static class GameFields
{
    static readonly FieldInfo? Huds = AccessTools.Field(typeof(EnemyHud), "m_huds");
    static readonly FieldInfo? HudInstance = AccessTools.Field(typeof(EnemyHud), "m_instance");
    static readonly FieldInfo? TombView = AccessTools.Field(typeof(TombStone), "m_nview");
    static readonly FieldInfo? TombText = AccessTools.Field(typeof(TombStone), "m_worldText");
    static FieldInfo? _hudName;
    static FieldInfo? _hudCharacter;

    public static EnemyHud? CurrentHud => HudInstance?.GetValue(null) as EnemyHud;

    /// <summary>The nameplates the game is drawing: character + its name text.</summary>
    public static void EachPlate(EnemyHud hud, Action<Character, TMP_Text> visit)
    {
        if (Huds?.GetValue(hud) is not IDictionary huds)
            return;
        foreach (DictionaryEntry e in huds)
        {
            if (e.Key is not Character character || e.Value == null)
                continue;
            _hudName ??= AccessTools.Field(e.Value.GetType(), "m_name");
            if (_hudName?.GetValue(e.Value) is TMP_Text text)
                visit(character, text);
        }
    }

    public static ZNetView? View(TombStone tomb) => TombView?.GetValue(tomb) as ZNetView;

    public static TMP_Text? WorldText(TombStone tomb) => TombText?.GetValue(tomb) as TMP_Text;
}
