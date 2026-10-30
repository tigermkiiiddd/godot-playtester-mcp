#nullable disable
using Godot;
namespace GodotPlaytester;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;


public partial class GameMcpServer
{
    // ── get_ui_layout (tree structure like DOM) ─────────────────────────

    private string GetUILayout(Dictionary<string, JsonElement> args)
    {
        bool visOnly = !args.ContainsKey("visible_only") || args["visible_only"].GetBoolean();
        bool taggedOnly = !args.ContainsKey("tagged_only") || args["tagged_only"].GetBoolean();
        int maxDepth = args.ContainsKey("max_depth") ? args["max_depth"].GetInt32() : 8;
        string path = args.ContainsKey("path") ? args["path"].GetString() : null;
        bool fullDetail = args.ContainsKey("detail") && args["detail"].GetString() == "full";

        string[] types = null;
        if (args.ContainsKey("types"))
        {
            var t = args["types"];
            if (t.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(t.GetString()))
                types = t.GetString().Split(',');
            else if (t.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in t.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(item.GetString()))
                        list.Add(item.GetString());
                if (list.Count > 0) types = list.ToArray();
            }
        }

        var tree = GetTree();
        if (tree?.Root == null) return "{\"error\":\"No scene tree\"}";

        // Report the server's main viewport at the top level. Individual controls are
        // classified against their own viewport and any clipping ancestors below.
        var viewportRect = GetViewport().GetVisibleRect();
        float vw = viewportRect.Size.X > 0 ? viewportRect.Size.X : DisplayServer.WindowGetSize().X;
        float vh = viewportRect.Size.Y > 0 ? viewportRect.Size.Y : DisplayServer.WindowGetSize().Y;

        var warnings = new JsonArray();
        var roots = new JsonArray();

        if (!string.IsNullOrEmpty(path))
        {
            // Local view: only inspect subtree at given path
            var target = GetNodeOrNull(path);
            if (target == null) return $"{{\"error\":\"Node not found: {path}\"}}";
            var sub = BuildUITreeNode(target, visOnly, types, 0, maxDepth, taggedOnly, fullDetail, warnings);
            if (sub != null) roots.Add(sub);
        }
        else
        {
            // Full view: all root children
            foreach (var child in tree.Root.GetChildren())
            {
                if (child == this) continue;
                var sub = BuildUITreeNode(child, visOnly, types, 0, maxDepth, taggedOnly, fullDetail, warnings);
                if (sub != null) roots.Add(sub);
            }
        }

        var result = new JsonObject
        {
            ["viewport"] = new JsonArray { Math.Round(vw, 0), Math.Round(vh, 0) },
            ["roots"] = roots
        };
        if (warnings.Count > 0) result["warnings"] = warnings;
        return JsonSerializer.Serialize(result, JsonOpts);
    }

    private JsonNode BuildUITreeNode(Node node, bool visOnly, string[] types, int depth, int maxDepth, bool taggedOnly, bool fullDetail, JsonArray warnings)
    {
        if (node == null || node == this) return null;
        if (depth > maxDepth) return null;

        if (node is Control c)
        {
            // Always skip mcp_ignore group
            if (c.IsInGroup("mcp_ignore")) return null;

            if (visOnly && !c.IsVisibleInTree()) return null;
            if (types != null && !types.Any(t => node.GetType().Name.Contains(t.Trim(), StringComparison.OrdinalIgnoreCase))) return null;

            // Tagged-only mode: if control is not interesting, pass through children instead
            if (taggedOnly && !IsInterestingControl(c))
            {
                // Try grid compression for homogeneous mcp_data children
                if (TryBuildCompactGrid(c, visOnly, fullDetail, out var passGrid))
                    return passGrid;

                var passChildren = new List<JsonNode>();
                foreach (var ch in c.GetChildren())
                {
                    var childNode = BuildUITreeNode(ch, visOnly, types, depth, maxDepth, taggedOnly, fullDetail, warnings);
                    if (childNode != null) passChildren.Add(childNode);
                }
                if (passChildren.Count == 1) return passChildren[0];
                if (passChildren.Count > 1)
                {
                    var arr = new JsonArray();
                    foreach (var cn in passChildren) arr.Add(cn);
                    return new JsonObject { ["children"] = arr };
                }
                return null;
            }

            var e = new JsonObject
            {
                ["type"] = node.GetType().Name,
                ["name"] = c.Name.ToString(),
                ["path"] = c.GetPath().ToString(),
                ["visible"] = c.IsVisibleInTree(),
                ["depth"] = depth
            };
            var r = c.GetGlobalRect();
            e["rect"] = new JsonArray { Math.Round(r.Position.X, 0), Math.Round(r.Position.Y, 0), Math.Round(r.Size.X, 0), Math.Round(r.Size.Y, 0) };
            e["center"] = new JsonArray { Math.Round(r.Position.X + r.Size.X / 2, 0), Math.Round(r.Position.Y + r.Size.Y / 2, 0) };

            // A geometry clue, not a verdict: compare in the control's own viewport and
            // narrow the bounds through clipping ancestors. Scroll content is expected to
            // move beyond its clip frame, so it stays inline but is suppressed from warnings.
            var visibleBounds = GetEffectiveVisibleBounds(c, out var clipSource, out var insideScrollContainer);
            var overflow = ClassifyRectVsBounds(r, visibleBounds);
            if (overflow != null)
            {
                overflow["reference"] = clipSource;
                e["offscreen"] = overflow;
                if (IsInterestingControl(c) && !insideScrollContainer)
                {
                    warnings.Add(new JsonObject
                    {
                        ["path"] = c.GetPath().ToString(),
                        ["name"] = c.Name.ToString(),
                        // JsonObject nodes are single-parent — read the scalar type string,
                        // don't re-reference the same overflow object that's now under e["offscreen"].
                        ["type"] = overflow["type"]?.ToString(),
                        ["rect"] = e["rect"].DeepClone(),
                        ["reference"] = clipSource,
                        ["severity"] = "diagnostic_hint"
                    });
                }
            }

            // Focus
            e["focused"] = c.HasFocus();

            // Editable (LineEdit / TextEdit)
            if (c is LineEdit le) { e["editable"] = le.Editable; e["text"] = le.Text; }
            else if (c is TextEdit te) { e["editable"] = te.Editable; e["text"] = te.Text; }

            // Mouse filter
            e["mouse_filter"] = c.MouseFilter.ToString();

            // Disabled
            if (c is BaseButton bb) e["disabled"] = bb.Disabled;

            // Type-specific fields
            string textContent = null;
            switch (c)
            {
                case CheckBox cb: e["pressed"] = cb.ButtonPressed; e["text"] = cb.Text; textContent = cb.Text; break;
                case OptionButton ob: e["selected"] = ob.Selected; e["item_count"] = ob.ItemCount; e["text"] = ob.Text; textContent = ob.Text; break;
                case Button b: e["text"] = b.Text; textContent = b.Text; break;
                case Label l: e["text"] = l.Text; textContent = l.Text; break;
                case ProgressBar pb: e["value"] = Math.Round(pb.Value, 1); e["max_value"] = Math.Round(pb.MaxValue, 1); break;
                case Slider s: e["value"] = Math.Round(s.Value, 2); e["min_value"] = Math.Round(s.MinValue, 2); e["max_value"] = Math.Round(s.MaxValue, 2); break;
                case ItemList il: e["item_count"] = il.ItemCount; break;
                case SpinBox sb: e["value"] = Math.Round(sb.Value, 2); e["min_value"] = Math.Round(sb.MinValue, 2); e["max_value"] = Math.Round(sb.MaxValue, 2); break;
                case TabBar tb: e["tab_count"] = tb.TabCount; e["current_tab"] = tb.CurrentTab; break;
            }

            // Font size + text rendered size (for controls with text)
            try
            {
                var fontSize = c.GetThemeFontSize("font_size");
                if (fontSize > 0)
                {
                    e["font_size"] = fontSize;
                    if (!string.IsNullOrEmpty(textContent))
                    {
                        var font = c.GetThemeFont("font");
                        var textSize = font.GetStringSize(textContent, HorizontalAlignment.Left, -1, fontSize);
                        e["text_size"] = new JsonArray { Math.Round(textSize.X, 0), Math.Round(textSize.Y, 0) };
                    }
                }
            }
            catch { }

            // Custom control domain data via SetMeta("mcp_data", jsonString)
            if (c.HasMeta("mcp_data"))
            {
                var v = c.GetMeta("mcp_data");
                if (v.VariantType == Variant.Type.String)
                {
                    try
                    {
                        var parsed = JsonNode.Parse(v.AsString());
                        if (parsed is JsonObject obj) e["data"] = obj;
                    }
                    catch { }
                }
            }

            // Recurse into children — try grid compression first
            if (TryBuildCompactGrid(c, visOnly, fullDetail, out var gridObj))
            {
                e["grid"] = gridObj;
                e["children_count"] = c.GetChildCount();
            }
            else
            {
                var childArr = new JsonArray();
                int childCount = 0;
                foreach (var ch in c.GetChildren())
                {
                    var childNode = BuildUITreeNode(ch, visOnly, types, depth + 1, maxDepth, taggedOnly, fullDetail, warnings);
                    if (childNode != null) { childArr.Add(childNode); childCount++; }
                }
                e["children_count"] = childCount;
                if (childCount > 0) e["children"] = childArr;
            }

            return e;
        }
        else
        {
            // Non-Control node: recurse looking for Control descendants
            var childNodes = new List<JsonNode>();
            foreach (var ch in node.GetChildren())
            {
                var childNode = BuildUITreeNode(ch, visOnly, types, depth, maxDepth, taggedOnly, fullDetail, warnings);
                if (childNode != null) childNodes.Add(childNode);
            }
            // Merge if only one child (skip non-UI intermediate nodes)
            if (childNodes.Count == 1) return childNodes[0];
            if (childNodes.Count > 1)
            {
                var arr = new JsonArray();
                foreach (var cn in childNodes) arr.Add(cn);
                return new JsonObject { ["children"] = arr };
            }
            return null;
        }
    }

    /// <summary>
    /// Detect grid pattern: all visible children are the same Control type with mcp_data.
    /// Returns compact columnar format — columns defined once, rows as value arrays.
    /// Eliminates repeated JSON keys for grid/table UIs (inventory, skill trees, etc.).
    /// </summary>
    private bool TryBuildCompactGrid(Control parent, bool visOnly, bool fullDetail, out JsonObject gridObj)
    {
        gridObj = null;
        var rawChildren = parent.GetChildren();
        if (rawChildren.Count < 2) return false;

        string cellType = null;
        var cells = new List<(Control ctrl, JsonObject data)>();

        foreach (var ch in rawChildren)
        {
            if (ch is not Control cc) return false;
            if (visOnly && !cc.Visible) continue;
            if (!cc.HasMeta("mcp_data")) return false;

            var v = cc.GetMeta("mcp_data");
            if (v.VariantType != Variant.Type.String) return false;

            var t = cc.GetType().Name;
            if (cellType == null) cellType = t;
            else if (t != cellType) return false;

            try
            {
                var parsed = JsonNode.Parse(v.AsString());
                if (parsed is not JsonObject obj) return false;
                cells.Add((cc, obj));
            }
            catch { return false; }
        }

        if (cells.Count < 2) return false;

        // columns: cx, cy + data keys from first cell
        // compact mode: only include core item fields (name, count, desc, rarity, row, col)
        // full mode: include all mcp_data fields
        var compactKeys = new HashSet<string> { "item_name", "item_count", "item_description", "item_rarity", "row", "col" };
        var cols = new JsonArray();
        cols.Add("cx");
        cols.Add("cy");
        var dataKeys = new List<string>();
        foreach (var kv in cells[0].data)
        {
            if (!fullDetail && !compactKeys.Contains(kv.Key)) continue;
            cols.Add(kv.Key);
            dataKeys.Add(kv.Key);
        }

        // rows: [cx, cy, ...data values]
        var rows = new JsonArray();
        foreach (var (ctrl, data) in cells)
        {
            var rect = ctrl.GetGlobalRect();
            var row = new JsonArray
            {
                Math.Round(rect.Position.X + rect.Size.X / 2, 0),
                Math.Round(rect.Position.Y + rect.Size.Y / 2, 0)
            };
            foreach (var key in dataKeys)
            {
                data.TryGetPropertyValue(key, out var val);
                row.Add(val?.DeepClone());
            }
            rows.Add(row);
        }

        gridObj = new JsonObject
        {
            ["cell_type"] = cellType,
            ["count"] = cells.Count,
            ["columns"] = cols,
            ["rows"] = rows
        };
        return true;
    }

    /// <summary>
    /// Whitelist: only include controls that are explicitly useful for AI.
    /// - Has mcp_data metadata (game code registered domain data)
    /// - In mcp_ui group (game code explicitly tagged)
    /// - Interactive control types (buttons, inputs, labels, etc.)
    /// Containers are included only if they have interesting children.
    /// </summary>
    private bool IsInterestingControl(Control c)
    {
        // Explicitly tagged → always include
        if (c.HasMeta("mcp_data") || c.IsInGroup("mcp_ui")) return true;

        // Interactive / data-bearing control types (whitelist)
        switch (c)
        {
            case CheckBox:      // before Button (inherits from it)
            case OptionButton:  // before Button (inherits from it)
            case Button:
            case Label:
            case LineEdit:
            case TextEdit:
            case ProgressBar:
            case Slider:
            case SpinBox:
            case TabBar:
            case ItemList:
            case TextureRect:
            case RichTextLabel:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Classify a control's global rect against effective visible bounds.
    /// Returns null if fully inside, otherwise one of:
    ///   "offscreen"  — rect lies entirely outside the viewport (not visible at all)
    ///   "clipped"    — rect partially exceeds an edge (part visible, part cut off)
    /// The returned object also carries which edges overflow and the pixel amounts,
    /// so callers can tell e.g. a label overflowing the bottom edge from one pushed
    /// off the left side entirely.
    /// </summary>
    private JsonObject ClassifyRectVsBounds(Rect2 r, Rect2 bounds)
    {
        float left = r.Position.X, top = r.Position.Y;
        float right = r.Position.X + r.Size.X, bottom = r.Position.Y + r.Size.Y;
        float boundLeft = bounds.Position.X, boundTop = bounds.Position.Y;
        float boundRight = bounds.Position.X + bounds.Size.X;
        float boundBottom = bounds.Position.Y + bounds.Size.Y;

        // Amount (px) each edge exceeds the viewport; >0 means it overflows.
        float overLeft = boundLeft - left;
        float overTop = boundTop - top;
        float overRight = right - boundRight;
        float overBottom = bottom - boundBottom;

        bool overflowsLeft = overLeft > 0.5f;
        bool overflowsTop = overTop > 0.5f;
        bool overflowsRight = overRight > 0.5f;
        bool overflowsBottom = overBottom > 0.5f;

        if (!overflowsLeft && !overflowsTop && !overflowsRight && !overflowsBottom) return null;

        // "offscreen" = no overlap at all (entirely beyond one edge)
        bool entirelyOutside =
            (right <= boundLeft) || (bottom <= boundTop) ||
            (left >= boundRight) || (top >= boundBottom);

        var o = new JsonObject { ["type"] = entirelyOutside ? "offscreen" : "clipped" };
        if (overflowsLeft) o["overflow_left"] = (float)Math.Round(overLeft, 0);
        if (overflowsTop) o["overflow_top"] = (float)Math.Round(overTop, 0);
        if (overflowsRight) o["overflow_right"] = (float)Math.Round(overRight, 0);
        if (overflowsBottom) o["overflow_bottom"] = (float)Math.Round(overBottom, 0);
        return o;
    }

    private Rect2 GetEffectiveVisibleBounds(Control control, out string source, out bool insideScrollContainer)
    {
        var viewport = control.GetViewport();
        var bounds = viewport?.GetVisibleRect() ?? new Rect2(Vector2.Zero, DisplayServer.WindowGetSize());
        if (bounds.Size.X <= 0 || bounds.Size.Y <= 0)
            bounds = new Rect2(Vector2.Zero, DisplayServer.WindowGetSize());

        source = "viewport";
        insideScrollContainer = false;
        for (Node ancestor = control.GetParent(); ancestor != null && ancestor != viewport; ancestor = ancestor.GetParent())
        {
            if (ancestor is not Control parentControl) continue;
            bool isScroll = parentControl is ScrollContainer;
            if (!parentControl.ClipContents && !isScroll) continue;

            insideScrollContainer |= isScroll;
            source = isScroll ? "scroll_clip" : "parent_clip";
            bounds = IntersectRects(bounds, parentControl.GetGlobalRect());
        }
        return bounds;
    }

    private static Rect2 IntersectRects(Rect2 a, Rect2 b)
    {
        float left = Math.Max(a.Position.X, b.Position.X);
        float top = Math.Max(a.Position.Y, b.Position.Y);
        float right = Math.Min(a.Position.X + a.Size.X, b.Position.X + b.Size.X);
        float bottom = Math.Min(a.Position.Y + a.Size.Y, b.Position.Y + b.Size.Y);
        return new Rect2(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    // ── UI Control Tools ─────────────────────────────────────────────────

    private string ClickElement(Dictionary<string, JsonElement> args)
    {
        var control = FindUIElement(args);
        if (control == null) return "{\"error\":\"Element not found. Provide 'name' or 'path'.\"}";

        var rect = control.GetGlobalRect();
        float ox = args.ContainsKey("offset_x") ? args["offset_x"].GetSingle() : 0f;
        float oy = args.ContainsKey("offset_y") ? args["offset_y"].GetSingle() : 0f;
        float x = (float)Math.Round(rect.Position.X + rect.Size.X / 2 + ox, 0);
        float y = (float)Math.Round(rect.Position.Y + rect.Size.Y / 2 + oy, 0);

        string mode = args.ContainsKey("mode") ? args["mode"].GetString() : "virtual";
        if (mode != "virtual" && mode != "os")
            return $"{{\"error\":\"Unsupported mouse mode: {mode}. Use virtual or os.\"}}";
        string button = args.ContainsKey("button") ? args["button"].GetString() : "left";
        if (!TryParseMouseButton(button, out var btnIdx))
            return $"{{\"error\":\"Unsupported mouse button: {button}. Use left, right, or middle.\"}}";

        bool force = args.ContainsKey("force") && args["force"].GetBoolean();
        if (control is BaseButton bb2 && !force)
        {
            if (bb2.Disabled) return "{\"error\":\"Button is disabled — a real player cannot click it. Pass force=true to bypass (not recommended).\"}";
            if (!bb2.IsVisibleInTree()) return "{\"error\":\"Button is not visible — pass force=true to bypass.\"}";
        }

        if (mode == "os")
        {
            if (force && control is BaseButton forcedButton && (forcedButton.Disabled || !forcedButton.IsVisibleInTree()))
                return "{\"error\":\"force=true can bypass disabled/hidden buttons only in virtual signal mode; OS input cannot force a real hit.\"}";
            var osResult = OsClickMouse(button, "click", x, y);
            if (osResult.StartsWith("{\"error\"")) return osResult;
            return $"{{\"ok\":true,\"name\":\"{control.Name}\",\"x\":{x},\"y\":{y},\"mode\":\"os\",\"delivery\":\"os\",\"force_requested\":{force.ToString().ToLowerInvariant()},\"force_effective\":false}}";
        }

        if (force && control is BaseButton bb)
        {
            bb.EmitSignal(BaseButton.SignalName.Pressed);
            _simMousePos = new Vector2(x, y);
            return $"{{\"ok\":true,\"name\":\"{control.Name}\",\"x\":{x},\"y\":{y},\"mode\":\"virtual\",\"delivery\":\"signal\",\"force_requested\":true,\"force_effective\":true}}";
        }

        var pos = new Vector2(x, y);
        var targetViewport = control.GetViewport();
        PushVirtualMouseButton(btnIdx, true, pos, viewport: targetViewport);
        PushVirtualMouseButton(btnIdx, false, pos, viewport: targetViewport);
        return $"{{\"ok\":true,\"name\":\"{control.Name}\",\"x\":{x},\"y\":{y},\"mode\":\"virtual\",\"delivery\":\"viewport\",\"force_requested\":{force.ToString().ToLowerInvariant()},\"force_effective\":false}}";
    }

    private string TypeText(Dictionary<string, JsonElement> args)
    {
        var target = args["target"].GetString();
        var text = args["text"].GetString();
        var mode = args.ContainsKey("mode") ? args["mode"].GetString() : "set";

        // Find by name or path
        var control = FindControlByName(target) ?? FindControlByPath(target);
        if (control == null) return $"{{\"error\":\"Text target not found: {target}\"}}";

        if (mode == "set")
        {
            if (control is LineEdit le) le.Text = text;
            else if (control is TextEdit te) te.Text = text;
            else return $"{{\"error\":\"Element {control.Name} is not a text input (type={control.GetType().Name})\"}}";
            return $"{{\"ok\":true,\"target\":\"{target}\",\"text\":\"{text}\",\"mode\":\"set\"}}";
        }

        // type mode: simulate keystrokes
        if (control is LineEdit || control is TextEdit)
        {
            control.GrabFocus();
            foreach (var ch in text)
            {
                var kc = MapCharToKey(ch);
                Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = kc, Unicode = (uint)ch });
                Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = kc, Unicode = (uint)ch });
            }
            return $"{{\"ok\":true,\"target\":\"{target}\",\"text\":\"{text}\",\"mode\":\"type\"}}";
        }
        return $"{{\"error\":\"Element {control.Name} is not a text input\"}}";
    }

    private string GetFocusedElement()
    {
        var tree = GetTree();
        if (tree?.Root == null) return "{\"error\":\"No scene tree\"}";
        var focused = FindFocusedControl(tree.Root);
        if (focused == null) return "{\"focused\":false}";
        var r = focused.GetGlobalRect();
        var o = new JsonObject
        {
            ["focused"] = true,
            ["type"] = focused.GetType().Name,
            ["name"] = focused.Name.ToString(),
            ["path"] = focused.GetPath().ToString(),
            ["rect"] = new JsonArray { Math.Round(r.Position.X, 0), Math.Round(r.Position.Y, 0), Math.Round(r.Size.X, 0), Math.Round(r.Size.Y, 0) }
        };
        return JsonSerializer.Serialize(o, JsonOpts);
    }

    private string SelectOption(Dictionary<string, JsonElement> args)
    {
        int index = args["index"].GetInt32();
        var control = FindUIElement(args);
        if (control == null) return "{\"error\":\"Element not found\"}";

        if (control is OptionButton ob)
        {
            if (index < 0 || index >= ob.ItemCount) return $"{{\"error\":\"Index {index} out of range (0-{ob.ItemCount - 1})\"}}";
            ob.Select(index);
            return $"{{\"ok\":true,\"selected\":{index},\"text\":\"{ob.GetItemText(index)}\"}}";
        }
        if (control is ItemList il)
        {
            if (index < 0 || index >= il.ItemCount) return $"{{\"error\":\"Index {index} out of range (0-{il.ItemCount - 1})\"}}";
            il.Select(index);
            return $"{{\"ok\":true,\"selected\":{index}}}";
        }
        return $"{{\"error\":\"Element {control.Name} is not OptionButton/ItemList (type={control.GetType().Name})\"}}";
    }

    private string Hover(float x, float y)
    {
        try
        {
            PushVirtualMouseMotion(new Vector2(x, y));
            return $"{{\"ok\":true,\"x\":{x},\"y\":{y},\"mode\":\"virtual\",\"delivery\":\"viewport\"}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    private string DoubleClick(string button, float x, float y, string mode = "virtual")
    {
        if (!TryParseMouseButton(button, out var btn))
            return $"{{\"error\":\"Unsupported mouse button: {button}. Use left, right, or middle.\"}}";
        if (mode != "virtual" && mode != "os")
            return $"{{\"error\":\"Unsupported mouse mode: {mode}. Use virtual or os.\"}}";
        if (mode == "os")
        {
            // Two rapid real clicks — Windows recognizes them as a double-click by
            // OS-level timing (GetDoubleClickTime), same as a real double-click would.
            var first = OsClickMouse(button, "click", x, y);
            if (first.StartsWith("{\"error\"")) return first;
            return OsClickMouse(button, "click", x, y);
        }
        try
        {
            var pos = new Vector2(x, y);
            for (int i = 0; i < 2; i++)
            {
                PushVirtualMouseButton(btn, true, pos, doubleClick: i == 1);
                PushVirtualMouseButton(btn, false, pos);
            }
            return $"{{\"ok\":true,\"button\":\"{button}\",\"x\":{x},\"y\":{y},\"mode\":\"virtual\",\"delivery\":\"viewport\"}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    private string DragMouse(Dictionary<string, JsonElement> args)
    {
        float fromX = args["from_x"].GetSingle();
        float fromY = args["from_y"].GetSingle();
        float toX = args["to_x"].GetSingle();
        float toY = args["to_y"].GetSingle();
        float duration = args.ContainsKey("duration") ? args["duration"].GetSingle() : 0.3f;
        string button = args.ContainsKey("button") ? args["button"].GetString() : "left";
        if (!TryParseMouseButton(button, out _))
            return $"{{\"error\":\"Unsupported mouse button: {button}. Use left, right, or middle.\"}}";

        // Use macro system for frame-by-frame drag
        var id = $"macro_{++_macroCounter:D3}";
        var steps = new List<MacroStep>
        {
            new MacroStep { Type = MacroStepType.Drag, X = fromX, Y = fromY, TargetX = toX, TargetY = toY, Duration = duration, Button = button }
        };
        _macros[id] = new MacroRun
        {
            Id = id, Name = "drag", Steps = steps, Status = "running",
            StartTime = Time.GetTicksMsec() / 1000.0
        };
        return $"{{\"ok\":true,\"macro_id\":\"{id}\",\"from\":[{fromX},{fromY}],\"to\":[{toX},{toY}],\"duration\":{duration}}}";
    }

    // ── UI helpers ───────────────────────────────────────────────────────

    private Control FindUIElement(Dictionary<string, JsonElement> args)
    {
        if (args.ContainsKey("path") && !string.IsNullOrEmpty(args["path"].GetString()))
            return FindControlByPath(args["path"].GetString());
        if (args.ContainsKey("name") && !string.IsNullOrEmpty(args["name"].GetString()))
            return FindControlByName(args["name"].GetString());
        return null;
    }

    private Control FindControlByName(string name)
    {
        var tree = GetTree();
        if (tree?.Root == null) return null;
        return tree.Root.FindChild(name, recursive: true, owned: false) as Control;
    }

    private Control FindControlByPath(string path)
    {
        try { return GetNodeOrNull<Control>(path); }
        catch { return null; }
    }

    private static Control FindFocusedControl(Node node)
    {
        if (node is Control c && c.HasFocus()) return c;
        foreach (var child in node.GetChildren())
        {
            var found = FindFocusedControl(child);
            if (found != null) return found;
        }
        return null;
    }
}
