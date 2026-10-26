using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;

namespace DrakesServerTeams;

/// <summary>
/// Reads and writes the two flat JSON files next to the BepInEx config. Server only.
/// Uses Jotunn's SimpleJson: Unity's JsonUtility writes "{}" for types in a plugin assembly loaded at runtime.
/// </summary>
public static class TeamStore
{
    public static string Folder => Path.Combine(Paths.ConfigPath, "DrakeMods.ServerTeams");
    public static string TeamsPath => Path.Combine(Folder, "teams.json");
    public static string RulesPath => Path.Combine(Folder, "deathrules.json");
    public static string MembersPath => Path.Combine(Folder, "members.json");

    public static TeamsFile LoadTeams(ManualLogSource log)
    {
        var file = Read<TeamsFile>(TeamsPath, log);
        if (file != null && !IsEmptyLeftover(TeamsPath))
            return file;
        // Present but unreadable: never overwrite an admin's edit with the starter file.
        if (file == null && File.Exists(TeamsPath))
            return new TeamsFile();

        // First run: write a starter file the admin edits. Two plain sides, nothing locked.
        file = new TeamsFile();
        file.teams.Add(new TeamDefinition { Id = "red", DisplayName = "Red", Color = "#C0392B" });
        file.teams.Add(new TeamDefinition { Id = "blue", DisplayName = "Blue", Color = "#2980B9" });
        Write(TeamsPath, file, log);
        log.LogInfo("Wrote a starter teams.json (red, blue). Edit it and restart the server.");
        return file;
    }

    /// <summary>The death rules. First run writes a starter file with the rules switched off.</summary>
    public static DeathRules LoadRules(ManualLogSource log)
    {
        var rules = Read<DeathRules>(RulesPath, log);
        if (rules != null)
            return rules;
        if (File.Exists(RulesPath))
            return new DeathRules();
        rules = new DeathRules();
        Write(RulesPath, rules, log);
        log.LogInfo("Wrote a starter deathrules.json (switched off). Edit it and use 'teams deathrules on' to try it.");
        return rules;
    }

    public static void SaveRules(DeathRules rules, ManualLogSource log) => Write(RulesPath, rules, log);

    public static void SaveTeams(TeamsFile file, ManualLogSource log) => Write(TeamsPath, file, log);

    public static MembersFile LoadMembers(ManualLogSource log) =>
        Read<MembersFile>(MembersPath, log) ?? new MembersFile();

    public static void SaveMembers(MembersFile file, ManualLogSource log) => Write(MembersPath, file, log);

    static T? Read<T>(string path, ManualLogSource log) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return SimpleJson.SimpleJson.DeserializeObject<T>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            // A bad edit must not silently wipe the file on the next save, so keep a copy.
            log.LogError($"Could not read {Path.GetFileName(path)}: {ex.Message}. Keeping a .bad copy and starting empty.");
            try { File.Copy(path, path + ".bad", true); } catch { /* best effort */ }
            return null;
        }
    }

    static void Write<T>(string path, T data, ManualLogSource log)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Indent(SimpleJson.SimpleJson.SerializeObject(data!)));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            log.LogError($"Could not write {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    /// <summary>A teams.json of just "{}": written by 0.1.0 builds whose serializer failed. Safe to replace with the starter file.</summary>
    static bool IsEmptyLeftover(string path)
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path).Trim() == "{}";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Pretty-prints compact JSON (two spaces) so admins can edit the files by hand.</summary>
    static string Indent(string json)
    {
        var sb = new StringBuilder(json.Length * 2);
        var depth = 0;
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inString)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < json.Length)
                    sb.Append(json[++i]);
                else if (c == '"')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    sb.Append(c);
                    break;
                case '{':
                case '[':
                    sb.Append(c);
                    if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']'))
                    {
                        sb.Append(json[++i]);
                        break;
                    }
                    sb.AppendLine().Append(' ', ++depth * 2);
                    break;
                case '}':
                case ']':
                    sb.AppendLine().Append(' ', --depth * 2).Append(c);
                    break;
                case ',':
                    sb.Append(c).AppendLine().Append(' ', depth * 2);
                    break;
                case ':':
                    sb.Append(": ");
                    break;
                default:
                    if (!char.IsWhiteSpace(c))
                        sb.Append(c);
                    break;
            }
        }

        return sb.AppendLine().ToString();
    }
}
