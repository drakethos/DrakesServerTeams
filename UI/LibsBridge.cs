using System;
using System.Reflection;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// Coexisting with RenameIt: RenameIt uses Shift+Use on pieces for its rename window, which is also how an admin opens a Team Ward's settings.
/// DrakeModsLibs (which RenameIt requires) keeps a small registry of pieces that have their own Shift+Use menu; we register the Team Ward there
/// by reflection, so there is no dependency on Libs: without it nothing happens, and without RenameIt nothing needs to.
/// </summary>
static class LibsBridge
{
    public static bool Registered { get; private set; }

    public static void TryRegister()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "DrakeModsLibs")
                    continue;
                var register = asm.GetType("DrakeModsLibs.API.PieceInteractionClaims")?.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
                if (register == null)
                    return; // an older Libs without the claims registry
                Func<Piece, bool> claims = piece => piece != null && piece.gameObject.name.StartsWith(TeamWard.PrefabName, StringComparison.Ordinal);
                register.Invoke(null, new object[] { "DrakesServerTeams", claims });
                Registered = true;
                return;
            }
        }
        catch (Exception ex)
        {
            TeamsNet.Warn($"Could not register the Team Ward with DrakeModsLibs: {ex.Message}");
        }
    }
}
