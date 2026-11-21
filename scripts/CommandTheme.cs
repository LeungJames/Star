using Godot;

namespace Star;

internal static class CommandTheme
{
    internal static readonly Color Ink = new("e9eef0");
    internal static readonly Color Muted = new("acbbc0");
    internal static readonly Color Cool = new("a1d5d6");
    internal static readonly Color Brass = new("eac18c");
    internal static readonly Color Alert = new("f29372");
    internal static readonly Color Edge = new("405157");

    internal static Theme Create()
    {
        var font = new SystemFont
        {
            FontNames = new[] { "Microsoft YaHei UI", "Noto Sans CJK SC", "Microsoft YaHei", "PingFang SC", "SimHei" },
        };
        var theme = new Theme { DefaultFont = font, DefaultFontSize = 15 };
        theme.SetColor("font_color", "Label", Ink);
        theme.SetColor("font_color", "Button", Ink);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", Brass);
        theme.SetColor("font_disabled_color", "Button", new Color("829197"));
        theme.SetStylebox("normal", "Button", Surface(new Color("263337"), Edge, 8, 5));
        theme.SetStylebox("hover", "Button", Surface(new Color("36464a"), Cool, 8, 5));
        theme.SetStylebox("pressed", "Button", Surface(new Color("46382d"), Brass, 8, 5));
        theme.SetStylebox("disabled", "Button", Surface(new Color("20282b"), new Color("384247"), 8, 5));
        var focus = Surface(Colors.Transparent, Cool, 8, 5);
        focus.DrawCenter = false;
        theme.SetStylebox("focus", "Button", focus);
        theme.SetStylebox("panel", "PanelContainer", Surface(new Color("181f23f5"), Edge, 12, 10));
        theme.SetStylebox("panel", "TooltipPanel", Surface(new Color("182025"), Edge, 9, 6));
        theme.SetStylebox("scroll", "VScrollBar", Surface(new Color("242e31"), Edge, 4, 0));
        theme.SetStylebox("grabber", "VScrollBar", Surface(new Color("647e82"), Edge, 4, 0));
        theme.SetStylebox("grabber_highlight", "VScrollBar", Surface(Cool, Edge, 4, 0));
        theme.SetStylebox("grabber_pressed", "VScrollBar", Surface(Brass, Edge, 4, 0));
        return theme;
    }

    internal static StyleBoxFlat Surface(Color background, Color edge, int horizontal, int vertical)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = edge };
        style.SetCornerRadiusAll(3);
        style.SetBorderWidthAll(1);
        style.SetContentMargin(Side.Left, horizontal);
        style.SetContentMargin(Side.Right, horizontal);
        style.SetContentMargin(Side.Top, vertical);
        style.SetContentMargin(Side.Bottom, vertical);
        return style;
    }

    internal static PanelContainer Panel(Theme theme, Color? accent = null)
    {
        var panel = new PanelContainer { Theme = theme, MouseFilter = Control.MouseFilterEnum.Stop };
        if (accent.HasValue)
        {
            var style = Surface(new Color("181f23fa"), accent.Value, 12, 10);
            style.BorderWidthTop = 2;
            panel.AddThemeStyleboxOverride("panel", style);
        }
        return panel;
    }

    internal static Label Label(string text, Theme theme, int size = 15, Color? color = null, bool wrap = false)
    {
        var label = new Label
        {
            Text = text,
            Theme = theme,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue)
        {
            label.AddThemeColorOverride("font_color", color.Value);
        }
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        return label;
    }

    internal static Button Button(string text, string tip = "")
    {
        return new Button
        {
            Text = text,
            TooltipText = tip,
            CustomMinimumSize = new Vector2(0f, 40f),
            FocusMode = Control.FocusModeEnum.None,
        };
    }

    internal static VBoxContainer Column(int separation)
    {
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    internal static HBoxContainer Row(int separation)
    {
        var box = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    internal static HFlowContainer Flow()
    {
        var row = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("h_separation", 5);
        row.AddThemeConstantOverride("v_separation", 5);
        return row;
    }

    internal static void Anchor(Control control, Control.LayoutPreset preset, float left, float top, float right, float bottom)
    {
        control.SetAnchorsAndOffsetsPreset(preset);
        control.OffsetLeft = left;
        control.OffsetTop = top;
        control.OffsetRight = right;
        control.OffsetBottom = bottom;
    }
}
