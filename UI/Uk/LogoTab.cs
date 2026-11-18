using System;
using System.Collections.Generic;
using System.Linq;
using DrakeModsLibs.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace DrakesServerTeams;

/// <summary>
/// The logo designer tab, laid out like a layers editor (in the spirit of Rock Band's and Guitar Hero's logo editors): the layer list is on
/// the left (the base at the bottom, symbols stacked above it), and one properties panel on the right shows ONLY the selected layer. Editing
/// only changes a draft and the preview; nothing reaches the team until "Apply to team" in the footer. Each team keeps its own draft, so
/// switching teams does not lose your work.
/// </summary>
internal static class LogoTab
{
    sealed class Draft
    {
        public LogoSpec Spec = new();
        public string Applied = "";
        public int Sel = -1;        // -1 = the base, otherwise the index in Spec.Layers
        public int ColorSlot;       // base layer only: 0 = fill, 1 = border
    }

    static readonly string[] Quick =
    {
        "#C0392B", "#E67E22", "#F1C40F", "#27AE60", "#16A085", "#2980B9", "#8E44AD", "#E84393", "#FFFFFF", "#95A5A6", "#34495E", "#000000",
    };

    static readonly Dictionary<string, Draft> Drafts = new(StringComparer.OrdinalIgnoreCase);
    static string _teamId = "";
    static Texture2D? _preview;
    static bool _dirty;

    // controls of the current build
    static VisualElement? _previewBox;
    static Label? _status;
    static Button? _applyButton;
    static Button? _revertButton;
    static UkColorPicker? _picker;
    static Action? _rebuild;
    static TeamDefinition? _team;
    static readonly List<Action> Refreshers = new();

    static Draft D => Drafts[_teamId];

    /// <summary>For tests: "team|sel N|layers N symbols|dirty or clean|the draft's spec".</summary>
    internal static string Describe() =>
        _teamId.Length > 0 && Drafts.TryGetValue(_teamId, out var d)
            ? $"{_teamId}|sel {d.Sel}|layers {d.Spec.Layers.Count} {string.Join(",", d.Spec.Layers.Select(l => l.Symbol))}|{(IsDirty(d) ? "dirty" : "clean")}|{d.Spec}"
            : "no draft";

    static Draft FreshFor(TeamDefinition team) => new()
    {
        Spec = LogoSpec.TryParse(team.Logo, out var parsed, out _) ? parsed : LogoSpec.Starter(LogoShapes.IsBase(team.Icon) ? team.Icon : "shield", team.Color),
        Applied = team.Logo ?? "",
    };

    static Draft GetDraft(TeamDefinition team)
    {
        if (!Drafts.TryGetValue(team.Id, out var d))
            return Drafts[team.Id] = FreshFor(team);
        // an untouched draft follows the team's logo when someone else changed it
        if (d.Spec.ToString() == d.Applied && d.Applied != (team.Logo ?? ""))
            return Drafts[team.Id] = FreshFor(team);
        return d;
    }

    static bool IsDirty(Draft d) => d.Spec.ToString() != d.Applied;

    static void MarkDirty()
    {
        _dirty = true;
        RefreshAll();
    }

    static void RefreshAll()
    {
        foreach (var r in Refreshers)
            r();
        if (_team != null && Drafts.ContainsKey(_teamId))
            UpdateFooter();
    }

    // ---- the tab -------------------------------------------------------------------------------------------------------

    public static void Build(VisualElement root, Action rebuild, VisualElement footer)
    {
        _rebuild = rebuild;
        Refreshers.Clear();
        var teams = TeamsAPI.GetTeams().ToList();
        if (_teamId.Length == 0 || teams.All(t => !string.Equals(t.Id, _teamId, StringComparison.OrdinalIgnoreCase)))
            _teamId = teams[0].Id;
        _team = teams.First(t => string.Equals(t.Id, _teamId, StringComparison.OrdinalIgnoreCase));
        var d = GetDraft(_team);
        if (d.Sel >= d.Spec.Layers.Count)
            d.Sel = d.Spec.Layers.Count - 1;

        root.Add(TeamsWindow.Heading("Logo designer"));
        var teamRow = TeamsWindow.WrapRow();
        foreach (var t in teams)
        {
            var team = t;
            var on = string.Equals(team.Id, _teamId, StringComparison.OrdinalIgnoreCase);
            var unsaved = Drafts.TryGetValue(team.Id, out var other) && IsDirty(other);
            teamRow.Add(TeamsWindow.Btn($"<color={team.Color}>■</color> {team.DisplayName}{(unsaved ? " <color=#f0c06a>●</color>" : "")}", () =>
            {
                _teamId = team.Id;
                _dirty = true;
                rebuild();
            }, on ? UkButtonKind.Selected : UkButtonKind.Secondary));
        }
        root.Add(teamRow);

        var columns = new VisualElement().Row(Align.FlexStart);
        columns.style.marginTop = 8;
        root.Add(columns);

        // ---- left: preview and layers -----------------------------------------------------------------------------------
        var left = new VisualElement().Column();
        left.style.width = 340;
        left.style.marginRight = 24;
        _previewBox = new VisualElement().Size(320, 320).Round(UkTheme.Radius).Border(UkTheme.Line).Fill(UkTheme.Field);
        _previewBox.style.backgroundSize = new BackgroundSize(Length.Percent(92), Length.Percent(92));
        _previewBox.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
        _previewBox.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
        _previewBox.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
        left.Add(_previewBox);
        PaintPreview();
        var thisBox = _previewBox;
        thisBox.schedule.Execute(() =>
        {
            if (_dirty)
                PaintPreview();
        }).Every(80).Until(() => _previewBox != thisBox);
        left.Add(TeamsWindow.Note("A preview only: nothing changes for the team until you press <b>Apply to team</b>."));

        var layersHead = new VisualElement().Row();
        layersHead.style.marginTop = 6;
        layersHead.style.marginBottom = 6;
        layersHead.Add(UkControls.Text("Layers", 19, UkTheme.Gold, heading: true, wrap: false));
        layersHead.Add(UkControls.Spacer());
        var add = TeamsWindow.Btn("+ Symbol", () =>
        {
            d.Spec.Layers.Add(new LogoLayer { Symbol = "star", Color = "#FFFFFF", Scale = 0.45f });
            d.Sel = d.Spec.Layers.Count - 1;
            _dirty = true;
            rebuild();
        });
        add.style.marginBottom = 0;
        UkControls.SetButtonEnabled(add, d.Spec.Layers.Count < LogoSpec.MaxLayers);
        layersHead.Add(add);
        left.Add(layersHead);
        for (var i = d.Spec.Layers.Count - 1; i >= 0; i--)
            left.Add(LayerRow(d, i));
        left.Add(BaseRow(d));
        columns.Add(left);

        // ---- right: only the selected layer -----------------------------------------------------------------------------
        var right = new VisualElement().Column().Grow();
        right.Add(d.Sel < 0 ? BaseProperties(d) : SymbolProperties(d, d.Sel));
        columns.Add(right);

        BuildFooter(footer, d);
        UpdateFooter();
    }

    // ---- layer list -------------------------------------------------------------------------------------------------------

    static VisualElement Row(bool selected)
    {
        var row = new VisualElement().Row();
        row.Pad(8, 10).Round(UkTheme.Radius).Border(selected ? UkTheme.Accent : UkTheme.LineSoft).Fill(selected ? UkTheme.Active : UkTheme.PanelDeep);
        row.style.marginBottom = 6;
        return row;
    }

    static VisualElement Swatch(float size, Func<string> color)
    {
        var s = new VisualElement { pickingMode = PickingMode.Ignore }.Size(size, size).Fixed().Round(6).Border(UkTheme.Line).Fill(TeamIcons.ParseColor(color()));
        Refreshers.Add(() => s.style.backgroundColor = TeamIcons.ParseColor(color()));
        return s;
    }

    static VisualElement LayerRow(Draft d, int index)
    {
        var l = d.Spec.Layers[index];
        var row = Row(d.Sel == index);
        var swatch = Swatch(30, () => IsRecolorable(l) ? l.Color : "#8F939C");
        swatch.style.marginRight = 10;
        row.Add(swatch);
        var name = UkControls.Text($"Symbol {index + 1}: {l.Symbol}", 16, UkTheme.Text, wrap: false);
        name.style.flexGrow = 1;
        row.Add(name);
        row.Add(MiniButton("▲", "Move up", index < d.Spec.Layers.Count - 1, () => Swap(d, index, index + 1)));
        row.Add(MiniButton("▼", "Move down", index > 0, () => Swap(d, index, index - 1)));
        row.Add(MiniButton("✕", "Remove this symbol", true, () =>
        {
            d.Spec.Layers.RemoveAt(index);
            d.Sel = Math.Min(d.Sel, d.Spec.Layers.Count - 1);
            _dirty = true;
            _rebuild?.Invoke();
        }));
        row.RegisterCallback<ClickEvent>(e =>
        {
            if (e.target is Button || d.Sel == index)
                return;
            d.Sel = index;
            _rebuild?.Invoke();
        });
        return row;
    }

    static VisualElement BaseRow(Draft d)
    {
        var row = Row(d.Sel < 0);
        var swatch = Swatch(30, () => d.Spec.BaseColor);
        swatch.style.marginRight = 10;
        row.Add(swatch);
        var name = UkControls.Text($"Base: {d.Spec.Base}", 16, UkTheme.Text, wrap: false);
        name.style.flexGrow = 1;
        row.Add(name);
        row.RegisterCallback<ClickEvent>(_ =>
        {
            if (d.Sel < 0)
                return;
            d.Sel = -1;
            _rebuild?.Invoke();
        });
        return row;
    }

    static Button MiniButton(string glyph, string tip, bool enabled, Action action)
    {
        var b = UkControls.MakeIconButton(glyph, action, tip, UkButtonKind.Ghost);
        b.style.width = 34;
        b.style.height = 34;
        b.style.marginLeft = 2;
        UkControls.SetButtonEnabled(b, enabled);
        return b;
    }

    static void Swap(Draft d, int a, int b)
    {
        (d.Spec.Layers[a], d.Spec.Layers[b]) = (d.Spec.Layers[b], d.Spec.Layers[a]);
        d.Sel = d.Sel == a ? b : d.Sel == b ? a : d.Sel;
        _dirty = true;
        _rebuild?.Invoke();
    }

    // ---- properties: the base ----------------------------------------------------------------------------------------------

    static VisualElement PropertiesCard(string title)
    {
        var card = UkControls.Card();
        card.style.paddingTop = card.style.paddingBottom = 14;
        card.style.paddingLeft = card.style.paddingRight = 18;
        var t = UkControls.Text(title, 21, UkTheme.Gold, heading: true, wrap: false);
        t.style.marginBottom = 8;
        card.Add(t);
        return card;
    }

    static VisualElement Section(string title)
    {
        var l = UkControls.Text(title, 16, UkTheme.TextMuted, heading: true, wrap: false);
        l.style.marginTop = 12;
        l.style.marginBottom = 6;
        return l;
    }

    static VisualElement BaseProperties(Draft d)
    {
        var card = PropertiesCard("Base shape");
        var shapes = TeamsWindow.WrapRow();
        foreach (var b in LogoShapes.Bases)
        {
            var name = b;
            shapes.Add(TeamsWindow.Btn(name, () =>
            {
                d.Spec.Base = name;
                _dirty = true;
                _rebuild?.Invoke();
            }, string.Equals(d.Spec.Base, name, StringComparison.OrdinalIgnoreCase) ? UkButtonKind.Selected : UkButtonKind.Secondary));
        }
        card.Add(shapes);

        card.Add(Section("Colors (pick one, then choose its color below)"));
        var tiles = new VisualElement().Row();
        tiles.Add(ColorTile("Fill", () => d.Spec.BaseColor, d.ColorSlot == 0, () => { d.ColorSlot = 0; _rebuild?.Invoke(); }));
        tiles.Add(ColorTile("Border", () => d.Spec.BorderColor, d.ColorSlot == 1, () => { d.ColorSlot = 1; _rebuild?.Invoke(); }));
        card.Add(tiles);

        var border = new UkSlider("Border width", 0f, 0.2f, d.Spec.BorderWidth, v => v.ToString("0.00"));
        border.Root.style.marginTop = 10;
        border.Changed = v =>
        {
            d.Spec.BorderWidth = Mathf.Round(v * 100f) / 100f;
            MarkDirty();
        };
        card.Add(border.Root);

        card.Add(PickerBlock(() => d.ColorSlot == 0 ? d.Spec.BaseColor : d.Spec.BorderColor, hex =>
        {
            if (d.ColorSlot == 0)
                d.Spec.BaseColor = hex;
            else
                d.Spec.BorderColor = hex;
        }));
        return card;
    }

    /// <summary>A big, labelled color: swatch, name and the hex, outlined in brass when it is the one being edited.</summary>
    static VisualElement ColorTile(string label, Func<string> color, bool selected, Action onClick)
    {
        var tile = new VisualElement().Row();
        tile.name = "tile-" + label;
        tile.Pad(10, 14).Round(UkTheme.Radius).Border(selected ? UkTheme.Accent : UkTheme.Line, selected ? 2 : 1).Fill(selected ? UkTheme.Active : UkTheme.PanelDeep);
        tile.style.marginRight = 12;
        tile.style.minWidth = 220;
        var sw = new VisualElement { pickingMode = PickingMode.Ignore }.Size(52, 52).Fixed().Round(8).Border(UkTheme.Text, 1).Fill(TeamIcons.ParseColor(color()));
        sw.style.marginRight = 12;
        tile.Add(sw);
        var col = new VisualElement { pickingMode = PickingMode.Ignore }.Column();
        col.Add(UkControls.Text(label, 17, UkTheme.Text, heading: true, wrap: false));
        var hex = UkControls.Text(color().ToUpperInvariant(), 16, UkTheme.TextBody, wrap: false);
        col.Add(hex);
        tile.Add(col);
        tile.RegisterCallback<ClickEvent>(_ =>
        {
            if (!selected)
                onClick();
        });
        Refreshers.Add(() =>
        {
            sw.style.backgroundColor = TeamIcons.ParseColor(color());
            hex.text = color().ToUpperInvariant();
        });
        return tile;
    }

    /// <summary>The color picker plus the quick swatches, editing whatever <paramref name="get"/> / <paramref name="set"/> point at.</summary>
    static VisualElement PickerBlock(Func<string> get, Action<string> set)
    {
        var block = new VisualElement().Row(Align.FlexStart);
        block.style.marginTop = 12;
        _picker = new UkColorPicker();
        _picker.SetColor(get());
        _picker.Picked = hex =>
        {
            set(hex.ToUpperInvariant());
            MarkDirty();
        };
        block.Add(_picker.Root);

        var quickCol = new VisualElement().Column();
        quickCol.style.marginLeft = 24;
        quickCol.Add(UkControls.Text("Quick colors", 15, UkTheme.TextMuted, wrap: false));
        var grid = TeamsWindow.WrapRow();
        grid.style.width = 4 * 50;
        grid.style.marginTop = 6;
        foreach (var hex in Quick)
        {
            var h = hex;
            var swatch = new VisualElement().Size(42, 42).Round(8).Border(UkTheme.Line).Fill(TeamIcons.ParseColor(h));
            swatch.name = "quick-" + h;
            swatch.style.marginRight = 8;
            swatch.style.marginBottom = 8;
            swatch.RegisterCallback<ClickEvent>(_ =>
            {
                set(h);
                _picker!.SetColor(h);
                MarkDirty();
            });
            grid.Add(swatch);
        }
        quickCol.Add(grid);
        block.Add(quickCol);
        return block;
    }

    // ---- properties: one symbol ---------------------------------------------------------------------------------------------

    static bool IsRecolorable(LogoLayer l)
    {
        if (LogoShapes.IsSymbol(l.Symbol))
            return true;
        var icon = ImportedIcons.Get(l.Symbol);
        return icon == null || icon.Recolor;
    }

    static VisualElement SymbolProperties(Draft d, int index)
    {
        var l = d.Spec.Layers[index];
        var card = PropertiesCard($"Symbol {index + 1}");

        card.Add(Section("Shape"));
        var grid = TeamsWindow.WrapRow();
        foreach (var s in LogoShapes.Symbols)
        {
            var name = s;
            var b = TeamsWindow.Btn(name, () => Pick(l, name), string.Equals(l.Symbol, name, StringComparison.OrdinalIgnoreCase) ? UkButtonKind.Selected : UkButtonKind.Secondary);
            b.style.height = 38;
            b.style.fontSize = 15;
            grid.Add(b);
        }
        card.Add(grid);
        var imported = ImportedIcons.List();
        if (imported.Count > 0)
        {
            card.Add(Section("Imported icons"));
            var importedRow = TeamsWindow.WrapRow();
            foreach (var (name, recolor) in imported)
            {
                var n = name;
                var b = TeamsWindow.Btn($"{n} ({(recolor ? "recolor" : "static")})", () => Pick(l, n),
                    string.Equals(l.Symbol, n, StringComparison.OrdinalIgnoreCase) ? UkButtonKind.Selected : UkButtonKind.Secondary);
                b.style.height = 38;
                b.style.fontSize = 15;
                importedRow.Add(b);
            }
            card.Add(importedRow);
        }
        if (!LogoShapes.IsSymbol(l.Symbol) && ImportedIcons.Get(l.Symbol) == null)
            card.Add(TeamsWindow.Note($"<color=#e0675c>The icon '{l.Symbol}' is not on this machine (it will not show).</color>"));

        card.Add(Section("Color"));
        if (IsRecolorable(l))
        {
            card.Add(ColorTile("Symbol color", () => l.Color, true, () => { }));
            card.Add(PickerBlock(() => l.Color, hex => l.Color = hex));
        }
        else
            card.Add(TeamsWindow.Note("This icon is static: it keeps its own colors."));

        card.Add(Section("Position and size"));
        card.Add(LayerSlider("Left / right", l.X, -1f, 1f, v => l.X = v));
        card.Add(LayerSlider("Down / up", l.Y, -1f, 1f, v => l.Y = v));
        card.Add(LayerSlider("Size", l.Scale, 0.1f, 1.6f, v => l.Scale = v));
        card.Add(LayerSlider("Rotate", l.Rotation, -180f, 180f, v => l.Rotation = v));
        var flip = new UkChip("Flip left / right");
        flip.SetOn(l.Flip);
        flip.Changed = on =>
        {
            l.Flip = on;
            MarkDirty();
        };
        flip.Root.style.marginTop = 8;
        card.Add(flip.Root);
        return card;
    }

    static VisualElement LayerSlider(string title, float value, float min, float max, Action<float> set)
    {
        var s = new UkSlider(title, min, max, value, v => v.ToString("0.00"));
        s.Root.style.marginTop = 4;
        s.Changed = v =>
        {
            set(Mathf.Round(v * 100f) / 100f);
            MarkDirty();
        };
        return s.Root;
    }

    static void Pick(LogoLayer l, string symbol)
    {
        l.Symbol = symbol;
        _dirty = true;
        _rebuild?.Invoke();
    }

    // ---- footer: apply / revert -----------------------------------------------------------------------------------------------

    static void BuildFooter(VisualElement footer, Draft d)
    {
        var team = _team!;
        _status = UkControls.Text("", 15, UkTheme.Gold, wrap: false);
        _status.enableRichText = true;
        _status.style.marginRight = 14;
        footer.Add(_status);

        var plain = TeamsWindow.Btn("Use the plain icon", () =>
        {
            TeamsWindow.Cmd($"clearlogo {team.Id}");
            Drafts.Remove(team.Id);
            _rebuild?.Invoke();
        }, UkButtonKind.Ghost);
        plain.style.marginBottom = 0;
        UkControls.SetButtonEnabled(plain, !string.IsNullOrEmpty(team.Logo));
        footer.Add(plain);

        _revertButton = TeamsWindow.Btn("Revert", () =>
        {
            Drafts.Remove(team.Id);
            _dirty = true;
            _rebuild?.Invoke();
        });
        _revertButton.style.marginBottom = 0;
        footer.Add(_revertButton);

        _applyButton = TeamsWindow.Btn("Apply to team", () =>
        {
            var spec = d.Spec.ToString();
            TeamsWindow.Cmd($"setlogo {team.Id} {spec}");
            d.Applied = spec;
            RefreshAll();
        }, UkButtonKind.Primary);
        _applyButton.style.marginBottom = 0;
        footer.Add(_applyButton);
    }

    static void UpdateFooter()
    {
        if (_applyButton == null || _team == null || !Drafts.TryGetValue(_teamId, out var d))
            return;
        var dirty = IsDirty(d);
        UkControls.SetButtonEnabled(_applyButton, dirty);
        UkControls.SetButtonEnabled(_revertButton!, dirty && !string.IsNullOrEmpty(d.Applied));
        if (_status != null)
            _status.text = dirty ? $"● Not applied yet ({_team.DisplayName})" : "";
    }

    // ---- preview ----------------------------------------------------------------------------------------------------------------

    static void PaintPreview()
    {
        _dirty = false;
        if (_previewBox == null || !Drafts.ContainsKey(_teamId))
            return;
        var old = _preview;
        _preview = LogoRenderer.Render(D.Spec, 256);
        _previewBox.style.backgroundImage = new StyleBackground(_preview);
        if (old != null)
            UnityEngine.Object.Destroy(old);
    }
}
