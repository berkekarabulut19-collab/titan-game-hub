using System;
using System.Windows.Media;

namespace TitanGameHub.Theme
{
    /// <summary>
    /// Centralized Steam-inspired Purple color palette and brush helpers.
    /// All UI elements should reference this class for consistent theming.
    /// </summary>
    public static class ThemeManager
    {
        // -- Steam Dark Purple palette --
        public static readonly Color DarkestNavy      = Color.FromRgb(0x13, 0x10, 0x1B);
        public static readonly Color DarkNavy         = Color.FromRgb(0x1A, 0x15, 0x26);
        public static readonly Color DarkerNavy       = Color.FromRgb(0x16, 0x12, 0x20);
        public static readonly Color DarkCard         = Color.FromRgb(0x22, 0x1B, 0x33);
        public static readonly Color SidebarNavyColor = Color.FromRgb(0x15, 0x11, 0x1F);
        public static readonly Color PanelBg          = Color.FromRgb(0x1E, 0x17, 0x2C);
        public static readonly Color PanelBgAlt       = Color.FromRgb(0x25, 0x1D, 0x38);
        public static readonly Color PanelBgDark      = Color.FromRgb(0x11, 0x0E, 0x18);
        public static readonly Color InputBg          = Color.FromRgb(0x0E, 0x0B, 0x14);
        public static readonly Color CardHover        = Color.FromRgb(0x2D, 0x23, 0x45);

        // -- Accent / Highlight palette (Purple + Steam Green Play) --
        public static readonly Color AccentTeal       = Color.FromRgb(0xC0, 0x84, 0xFC);
        public static readonly Color AccentBlue       = Color.FromRgb(0x93, 0x33, 0xEA);
        public static readonly Color AccentGreen      = Color.FromRgb(0x5D, 0xBA, 0x2F);
        public static readonly Color AccentGreenHi    = Color.FromRgb(0x6E, 0xD4, 0x3B);
        public static readonly Color AccentGold       = Color.FromRgb(0xFF, 0xCC, 0x00);

        // -- Text / Foreground palette --
        public static readonly Color TextPrimary      = Color.FromRgb(0xE4, 0xDF, 0xF0);
        public static readonly Color TextSecondary    = Color.FromRgb(0xBF, 0xB8, 0xD1);
        public static readonly Color TextMuted        = Color.FromRgb(0x94, 0x8B, 0xA8);
        public static readonly Color TextDim          = Color.FromRgb(0x6B, 0x60, 0x82);
        public static readonly Color TextLight        = Color.FromRgb(0xD6, 0xCF, 0xF0);

        // -- Border / Separator palette --
        public static readonly Color BorderDark       = Color.FromRgb(0x0D, 0x0A, 0x14);
        public static readonly Color BorderMed        = Color.FromRgb(0x3B, 0x2D, 0x59);
        public static readonly Color BorderLight      = Color.FromRgb(0x53, 0x3E, 0x7D);
        public static readonly Color BorderCard       = Color.FromRgb(0x2E, 0x23, 0x47);

        // -- Status colors --
        public static readonly Color StatusError      = Color.FromRgb(0xFF, 0x6B, 0x6B);
        public static readonly Color StatusWarn       = Color.FromRgb(0xD9, 0x77, 0x06);
        public static readonly Color StatusDanger     = Color.FromRgb(0xDC, 0x26, 0x26);

        // -- Brush helpers --
        public static Brush B(Color c) { return new SolidColorBrush(c); }
        public static Brush Hex(string hex) { return (SolidColorBrush)(new BrushConverter().ConvertFromString(hex)); }

        // Pre-made brushes (Brush properties for XAML/UI usage)
        public static Brush DarkestNavyBrush    { get { return B(DarkestNavy); } }
        public static Brush DarkNavyBrush       { get { return B(DarkNavy); } }
        public static Brush DarkerNavyBrush     { get { return B(DarkerNavy); } }
        public static Brush DarkCardBrush       { get { return B(DarkCard); } }
        // Brush properties that old code expects by name
        public static Brush SidebarNavy         { get { return B(SidebarNavyColor); } }
        public static Brush PanelBgBrush        { get { return B(PanelBg); } }
        public static Brush PanelBgAltBrush     { get { return B(PanelBgAlt); } }
        public static Brush PanelBgDarkBrush    { get { return B(PanelBgDark); } }
        public static Brush InputBgBrush        { get { return B(InputBg); } }
        public static Brush CardHoverBrush      { get { return B(CardHover); } }

        public static Brush AccentTealBrush     { get { return B(AccentTeal); } }
        public static Brush AccentBlueBrush     { get { return B(AccentBlue); } }
        public static Brush AccentGreenBrush    { get { return B(AccentGreen); } }
        public static Brush AccentGreenHiBrush  { get { return B(AccentGreenHi); } }

        public static Brush TextPrimaryBrush    { get { return B(TextPrimary); } }
        public static Brush TextSecondaryBrush  { get { return B(TextSecondary); } }
        public static Brush TextMutedBrush      { get { return B(TextMuted); } }
        public static Brush TextDimBrush        { get { return B(TextDim); } }
        public static Brush TextLightBrush      { get { return B(TextLight); } }

        public static Brush BorderDarkBrush     { get { return B(BorderDark); } }
        public static Brush BorderMedBrush      { get { return B(BorderMed); } }
        public static Brush BorderLightBrush    { get { return B(BorderLight); } }
        public static Brush BorderCardBrush     { get { return B(BorderCard); } }

        public static Brush StatusErrorBrush    { get { return B(StatusError); } }
        public static Brush StatusWarnBrush     { get { return B(StatusWarn); } }
        public static Brush StatusDangerBrush   { get { return B(StatusDanger); } }

        public static readonly Color BorderPrimaryColor = BorderMed;   // alias
        public static readonly Color ForegroundMainColor = TextPrimary; // alias
        public static Brush BorderPrimaryBrush    { get { return B(BorderMed); } }
        public static Brush BorderSubtleBrush     { get { return B(BorderDark); } }
        public static Brush ForegroundMainBrush   { get { return B(TextPrimary); } }
        public static Brush ForegroundSubBrush    { get { return B(TextMuted); } }
        public static Brush ForegroundMutedBrush  { get { return B(TextDim); } }
        public static Brush BorderAccentBrush     { get { return B(AccentTeal); } }
    }
}
