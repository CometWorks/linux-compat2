namespace ClientPlugin.Settings;

internal static class SettingsLayout
{
    public static double DialogWidth = 760;
    public static double DialogHeight = 840;

    public static double LabelColumnWidth = 180;

    // Minimum height for each value row. Matches ControlHeight so every row
    // is exactly the same tall regardless of which control it contains; since
    // ControlHeight is a fixed size on the focusable controls themselves,
    // focus/hover border thickness changes are drawn inside the control and
    // cannot grow the row past this minimum.
    public static double RowMinHeight = 52;

    // Fixed height applied to focusable controls (TextBox, ComboBox, Button)
    // so their outer bounds do not change when the theme swaps in a thicker
    // border on focus/hover — the thicker border is drawn inside the fixed
    // box instead, which keeps the control visually pinned in the row. Needs
    // to be large enough that the content (at the inherited 18 pt font size,
    // including descenders) fits with the theme's default padding.
    public static double ControlHeight = 52;
}
