using System;
using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// Team banners and flags: pieces built from the Blender models in <c>Models/</c> (embedded .glb, read by <see cref="GlbModel"/>).
/// The cloth carries the builder's team logo (see <see cref="TeamBannerVisual"/>). The team is the server-signed stamp every
/// piece of this mod uses; a banner built by someone without a team is grey. Carry and capture mechanics are not here.
/// </summary>
public static class TeamBanners
{
    public const string Hanging = "TeamBannerHanging";
    public const string Standing = "TeamBannerStanding";
    public const string Flag = "TeamFlag";
    /// <summary>The carried banner: an item (not a piece) held in the hand, painted for the player who carries it.</summary>
    public const string Carry = "TeamBannerCarry";

    sealed class Def
    {
        public string Prefab = "";
        public string Model = "";
        public string Display = "";
        public string Description = "";
        public BannerTextures.Shape Shape;
        public bool GroundOnly;
        public int Wood;
        public int Leather;
    }

    static readonly Def[] Defs =
    {
        new() { Prefab = Hanging, Model = "banner_hanging", Display = "Team Banner (hanging)", Description = "A banner with your team's logo, on a crossbar. Hang it on a wall.",
                Shape = BannerTextures.Shape.Tall, Wood = 4, Leather = 2 },
        new() { Prefab = Standing, Model = "banner_standing", Display = "Team Banner (standing)", Description = "A banner with your team's logo on its own pole and foot.",
                Shape = BannerTextures.Shape.Tall, GroundOnly = true, Wood = 8, Leather = 3 },
        new() { Prefab = Flag, Model = "flag_standard", Display = "Team Flag", Description = "A tall flagpole with your team's logo on the flag.",
                Shape = BannerTextures.Shape.Wide, GroundOnly = true, Wood = 8, Leather = 3 },
    };

    static bool _registered;

    public static bool IsBanner(string? prefabName) =>
        prefabName != null && (prefabName.StartsWith("TeamBanner", StringComparison.Ordinal) || prefabName.StartsWith(Flag, StringComparison.Ordinal));

    /// <summary>The team a banner piece was built under (server-signed stamp), or null.</summary>
    public static string? TeamOf(GameObject banner)
    {
        var view = banner.GetComponentInParent<ZNetView>();
        return view != null && view.IsValid() ? PieceTeams.ReadTeam(view.GetZDO()) : null;
    }

    internal static void Register()
    {
        PrefabManager.OnVanillaPrefabsAvailable += RegisterAll;
    }

    static void RegisterAll()
    {
        if (_registered)
            return;
        _registered = true;
        PrefabManager.OnVanillaPrefabsAvailable -= RegisterAll;
        var strings = new Dictionary<string, string>();
        foreach (var def in Defs)
        {
            try
            {
                RegisterOne(def, strings);
            }
            catch (Exception ex)
            {
                TeamsNet.Warn($"{def.Display} could not be registered: {ex.Message}");
            }
        }
        LocalizationManager.Instance.GetLocalization().AddTranslation("English", strings);
        try
        {
            RegisterCarry();
        }
        catch (Exception ex)
        {
            TeamsNet.Warn($"The carried banner could not be registered: {ex.Message}");
        }
    }

    /// <summary>
    /// The carried banner is a Torch clone (held upright in the hand, no attack animation to fight) wearing the Blender model. The cloth is
    /// painted with the carrier's team (<see cref="CarriedBannerVisual"/>); lying on the ground it is grey. Carrying and capturing rules
    /// belong to the War extension; this is the object.
    /// </summary>
    static void RegisterCarry()
    {
        if (!GlbModel.TryLoad("banner_carry", out var mesh, out var materials, out var error) || mesh == null)
        {
            TeamsNet.Warn($"Carried banner: model not loaded ({error}).");
            return;
        }
        var item = new CustomItem(Carry, "Torch", new ItemConfig
        {
            Name = "Team Banner (carried)",
            Description = "A light banner pole that shows the logo of the team carrying it.",
            CraftingStation = "piece_workbench",
            Requirements = new[]
            {
                new RequirementConfig("Wood", 4),
                new RequirementConfig("LeatherScraps", 2),
            },
        });
        var prefab = item.ItemPrefab;

        // out with the torch's flame, light and mesh
        // the torch's flame scripts point at the light and particles that are about to go
        foreach (var mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && (mb.GetType().Name == "LightFlicker" || mb.GetType().Name == "LightLod" || mb.GetType().Name == "Flicker"))
                UnityEngine.Object.DestroyImmediate(mb);
        foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            UnityEngine.Object.DestroyImmediate(ps);
        foreach (var l in prefab.GetComponentsInChildren<Light>(true))
            UnityEngine.Object.DestroyImmediate(l);
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            UnityEngine.Object.DestroyImmediate(r);

        var shared = prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
        shared.m_useDurability = false;
        shared.m_durabilityDrain = 0f;
        shared.m_weight = 3f;
        shared.m_maxStackSize = 1;
        var tex = BannerTextures.Get(null, BannerTextures.Shape.Tall);
        shared.m_icons = new[] { Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)) };

        // the model hangs from the hand: the grip (1.1 m up the pole) sits at the attach point
        var attach = prefab.transform.Find("attach") ?? prefab.transform;
        var model = new GameObject("banner");
        model.transform.SetParent(attach, false);
        // Held items point along +Z in the hand (the torch's own model is turned 90 degrees about X to get there), so the pole, which runs
        // along Y in the model, is turned the same way and moved so the grip (1.1 m up the pole) sits at the hand.
        model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        model.transform.localPosition = new Vector3(0f, 0f, -1.1f);
        model.AddComponent<MeshFilter>().sharedMesh = mesh;
        model.AddComponent<MeshRenderer>().sharedMaterials = materials;
        var visual = model.AddComponent<CarriedBannerVisual>();
        visual.ClothShape = BannerTextures.Shape.Tall;

        ItemManager.Instance.AddItem(item);
    }

    static void RegisterOne(Def def, Dictionary<string, string> strings)
    {
        if (!GlbModel.TryLoad(def.Model, out var mesh, out var materials, out var error) || mesh == null)
        {
            TeamsNet.Warn($"{def.Display}: model {def.Model} not loaded ({error}).");
            return;
        }

        var prefab = PrefabManager.Instance.CreateEmptyPrefab(def.Prefab, true);
        var view = prefab.GetComponent<ZNetView>();
        view.m_persistent = true;
        view.m_distant = false;

        // Jotunn's empty prefab carries a placeholder box (mesh, renderer, collider) that draws with the game's error shader: remove it.
        foreach (var c in prefab.GetComponents<Collider>())
            UnityEngine.Object.DestroyImmediate(c);
        UnityEngine.Object.DestroyImmediate(prefab.GetComponent<MeshRenderer>());
        UnityEngine.Object.DestroyImmediate(prefab.GetComponent<MeshFilter>());

        var model = new GameObject("model");
        model.transform.SetParent(prefab.transform, false);
        model.AddComponent<MeshFilter>().sharedMesh = mesh;
        model.AddComponent<MeshRenderer>().sharedMaterials = materials;

        // one box for the whole banner: hover, hammer removal. Not solid, you can walk through the cloth.
        var col = model.AddComponent<BoxCollider>();
        col.center = mesh.bounds.center;
        col.size = mesh.bounds.size;
        var layer = LayerMask.NameToLayer("piece_nonsolid");
        if (layer >= 0)
            model.layer = layer;

        var visual = prefab.AddComponent<TeamBannerVisual>();
        visual.ClothShape = def.Shape;
        visual.DisplayName = def.Display;

        var piece = prefab.AddComponent<Piece>();
        piece.m_name = "$piece_" + def.Prefab.ToLowerInvariant();
        piece.m_description = "$piece_" + def.Prefab.ToLowerInvariant() + "_desc";
        piece.m_groundOnly = def.GroundOnly;
        piece.m_clipEverything = !def.GroundOnly; // a wall banner is meant to sit against a wall
        piece.m_canBeRemoved = true;
        piece.m_icon = MakeIcon(def.Shape);

        strings["piece_" + def.Prefab.ToLowerInvariant()] = def.Display;
        strings["piece_" + def.Prefab.ToLowerInvariant() + "_desc"] = def.Description;

        PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, new PieceConfig
        {
            PieceTable = "Hammer",
            Category = "Misc",
            Requirements = new[]
            {
                new RequirementConfig("Wood", def.Wood, 0, true),
                new RequirementConfig("LeatherScraps", def.Leather, 0, true),
            },
        }));
    }

    /// <summary>The build-menu icon: a neutral banner picture.</summary>
    static Sprite MakeIcon(BannerTextures.Shape shape)
    {
        var tex = BannerTextures.Get(null, shape);
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }
}
