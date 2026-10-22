using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>Reads and writes the two flat JSON files next to the BepInEx config. Server only.</summary>
public static class TeamStore
{
    public static string Folder => Path.Combine(Paths.ConfigPath, "DrakeMods.ServerTeams");
    public static string TeamsPath => Path.Combine(Folder, "teams.json");
    public static string MembersPath => Path.Combine(Folder, "members.json");

    public static TeamsFile LoadTeams(ManualLogSource log)
    {
        var file = Read<TeamsFile>(TeamsPath, log);
        if (file != null)
            return file;
        // Present but unreadable: never overwrite an admin's edit with the starter file.
        if (File.Exists(TeamsPath))
            return new TeamsFile();

        // First run: write a starter file the admin edits. Two plain sides, nothing locked.
        file = new TeamsFile();
        file.teams.Add(new TeamDefinition { Id = "red", DisplayName = "Red", Color = "#C0392B" });
        file.teams.Add(new TeamDefinition { Id = "blue", DisplayName = "Blue", Color = "#2980B9" });
        Write(TeamsPath, file, log);
        log.LogInfo("Wrote a starter teams.json (red, blue). Edit it and restart the server.");
        return file;
    }

    public static MembersFile LoadMembers(ManualLogSource log) =>
        Read<MembersFile>(MembersPath, log) ?? new MembersFile();

    public static void SaveMembers(MembersFile file, ManualLogSource log) => Write(MembersPath, file, log);

    static T? Read<T>(string path, ManualLogSource log) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonUtility.FromJson<T>(File.ReadAllText(path));
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
            File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            log.LogError($"Could not write {Path.GetFileName(path)}: {ex.Message}");
        }
    }
}
