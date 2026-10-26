using System;
using System.Globalization;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.App.IoC;

namespace PCL.Core.UI.Theme;

/// <summary>
/// 配色模式更改事件。
/// </summary>
/// <param name="isDarkMode">当前是否为深色模式</param>
/// <param name="theme">当前配色主题</param>
public delegate void ColorModeChangedEvent(bool isDarkMode, ColorTheme theme);

/// <summary>
/// 配色主题更改事件。
/// </summary>
/// <param name="theme">当前配色主题</param>
public delegate void ColorThemeChangedEvent(ColorTheme theme);

[LifecycleScope("theme", "主题", false)]
[LifecycleService(LifecycleState.WindowCreating)]
public sealed partial class ThemeService
{
    [AnyConfigItem<ToneProfileConfig>("UiToneProfiles", ConfigSource.Local)]
    public static partial ToneProfileConfig ToneProfiles { get; set; }

    [RegisterConfigEvent]
    public static ConfigEventRegistry OnColorModeConfigChanged => new(
        scope: Config.Preference.Theme.ColorModeConfig,
        trigger: ConfigEvent.Update,
        handler: _ => RefreshColorMode()
    );

    [RegisterConfigEvent]
    public static ConfigEventRegistry OnColorThemeConfigChanged => new(
        scope: [
            Config.Preference.Theme.DarkColorConfig,
            Config.Preference.Theme.LightColorConfig,
            Config.Preference.Theme.DarkColorCustomConfig,
            Config.Preference.Theme.LightColorCustomConfig
        ],
        trigger: ConfigEvent.Update,
        handler: e =>
        {
            // ignore no change or non-current color theme change
            if (e.OldValue == e.Value) return;
            if (IsDarkMode)
            {
                if (e.Item == Config.Preference.Theme.LightColorConfig ||
                    e.Item == Config.Preference.Theme.LightColorCustomConfig) return;
            }
            else
            {
                if (e.Item == Config.Preference.Theme.DarkColorConfig ||
                    e.Item == Config.Preference.Theme.DarkColorCustomConfig) return;
            }
            // trigger color refresh
            if (Lifecycle.CurrentState > LifecycleState.Loading)
            {
                Lifecycle.CurrentApplication.Dispatcher.BeginInvoke(() =>
                {
                    ApplyColorResources();
                    ColorThemeChanged?.Invoke(CurrentTheme);
                    _AprilFoolLogic();
                });
            }
        }
    );

    [LifecycleStart]
    private static void _Start()
    {
        IsDarkMode = _IsDarkMode();
        _LogStatus();
        _RefreshAll();
    }

    private static void _LogStatus()
    {
        Context.Debug($"当前状态: {(IsDarkMode ? "Dark" : "Light")}, {CurrentTheme}");
    }

    private static bool _IsDarkMode() => Config.Preference.Theme.ColorMode switch
    {
        ColorMode.Light => false,
        ColorMode.Dark => true,
        ColorMode.System => SystemThemeHelper.IsSystemInDarkMode(),
        _ => false
    };

    /// <summary>
    /// 当前是否为深色主题。
    /// </summary>
    public static bool IsDarkMode { get; private set; }

    /// <summary>
    /// 配色模式更改事件。
    /// </summary>
    public static event ColorModeChangedEvent? ColorModeChanged;

    /// <summary>
    /// 配色主题更改事件。
    /// </summary>
    public static event ColorThemeChangedEvent? ColorThemeChanged;

    private static void _RefreshAll()
    {
        ApplyGrayResources();
        ApplyColorResources();
        ColorModeChanged?.Invoke(IsDarkMode, CurrentTheme);
        _AprilFoolLogic();
    }

    private static void _AprilFoolLogic()
    {
        // for HMCL theme on April Fools' Day
        if (Basics.IsAprilFool) Config.Preference.WindowTitleTypeConfig.TriggerEvent(ConfigEvent.Changed, null);
        else if (CurrentTheme == ColorTheme.HmclBlue) CurrentTheme = ColorTheme.CatBlue;
    }

    /// <summary>
    /// 刷新配色模式，若检测到当前配色模式有实际更改，则会触发主题刷新。
    /// </summary>
    public static void RefreshColorMode()
    {
        var isDarkMode = _IsDarkMode();
        if (IsDarkMode == isDarkMode) return;
        Context.Info("正在更改配色模式");
        IsDarkMode = isDarkMode;
        _LogStatus();
        if (Lifecycle.CurrentState > LifecycleState.Loading)
        {
            Lifecycle.CurrentApplication.Dispatcher.BeginInvoke(_RefreshAll);
        }
    }

    /// <summary>
    /// 当前使用的色彩属性。
    /// </summary>
    public static ToneProfile CurrentTone => IsDarkMode ? ToneProfiles.Dark : ToneProfiles.Light;

    /// <summary>
    /// 当前使用的主题色。
    /// </summary>
    public static ColorTheme CurrentTheme
    {
        get
        {
            var theme = Config.Preference.Theme;
            return IsDarkMode ? theme.DarkColor : theme.LightColor;
        }
        set
        {
            var theme = Config.Preference.Theme;
            var config = IsDarkMode ? theme.DarkColorConfig : theme.LightColorConfig;
            config.SetValue(value);
        }
    }

    /// <summary>
    /// 获取当前色彩主题对应的各种参数。
    /// </summary>
    public static (int Hue, double LightAdjust, double ChromaAdjust) GetCurrentThemeArgs()
    {
        var theme = Config.Preference.Theme;
        var customColor = IsDarkMode ? theme.DarkColorCustom : theme.LightColorCustom;
        if (TryParseHexColor(customColor, out var color)) return GetArgsFromColor(color);
        return GetPresetThemeArgs(CurrentTheme);
    }

    /// <summary>
    /// 获取指定配色模式当前实际显示的主题色，用于 UI 展示。
    /// </summary>
    /// <param name="darkMode">是否获取暗色模式的配色。</param>
    public static Color GetThemeDisplayColor(bool darkMode)
    {
        var theme = Config.Preference.Theme;
        if (TryParseHexColor(darkMode ? theme.DarkColorCustom : theme.LightColorCustom, out var color))
            return color;
        var tone = darkMode ? ToneProfiles.Dark : ToneProfiles.Light;
        var preset = GetPresetThemeArgs(darkMode ? theme.DarkColor : theme.LightColor);
        return LabColor.FromLch(_AdjustLinear(tone.L4, preset.LightAdjust),
            _AdjustLinear(tone.C4, preset.ChromaAdjust), preset.Hue).ToWpfColor();
    }

    /// <summary>
    /// 解析 <c>#RRGGBB</c> 格式的颜色字符串。
    /// </summary>
    public static bool TryParseHexColor(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.Trim().TrimStart('#');
        if (value.Length != 6) return false;
        if (!int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static (int Hue, double LightAdjust, double ChromaAdjust) GetPresetThemeArgs(ColorTheme theme)
        => theme switch
        {
            ColorTheme.SkyBlue => (235, 0.36, 0.2),
            ColorTheme.CatBlue => (255, 0, -0.2),
            ColorTheme.DeathBlue => (268, -0.05, -0.1),
            ColorTheme.HmclBlue => (275, -0.03, -0.35),
#if DEBUG
            _ => ((int)theme, 0, 0)
#else
            _ => throw new IndexOutOfRangeException($"Invalid theme index: {(int)theme}")
#endif
        };

    private static (int Hue, double LightAdjust, double ChromaAdjust) GetArgsFromColor(Color color)
    {
        var lab = LabColor.FromRgb(color.R, color.G, color.B);
        var chroma = Math.Sqrt(lab.A * lab.A + lab.B * lab.B);
        var hue = Math.Atan2(lab.B, lab.A) * 180d / Math.PI;
        if (hue < 0d) hue += 360d;
        var tone = CurrentTone;
        return ((int)Math.Round(hue) % 360, _InverseAdjustLinear(tone.L4, lab.L),
            _InverseAdjustLinear(tone.C4, chroma));
    }

    /// <summary>
    /// 计算 <see cref="_AdjustLinear" /> 的反函数。
    /// </summary>
    private static double _InverseAdjustLinear(double value, double target)
    {
        value = Math.Clamp(value, 0d, 1d);
        target = Math.Clamp(target, 0d, 1d);
        if (Math.Abs(target - value) < 1e-6d) return 0d;
        var adjust = target > value
            ? 1d - value <= 1e-6d ? 0d : (target - value) / (1d - value)
            : value <= 1e-6d ? 0d : (target - value) / value;
        return Math.Clamp(adjust, -1d, 1d);
    }

    private static double _AdjustLinear(double value, double adjustment)
    {
        if (adjustment == 0) return value;
        // 确保输入在合理范围内
        value = Math.Clamp(value, 0.0, 1.0);
        adjustment = Math.Clamp(adjustment, -1.0, 1.0);
        // 非对称线性插值
        return adjustment switch
        {
            > 0 => value + (1.0 - value) * adjustment,
            _ => value + value * adjustment
        };
    }

    private static CatColorResource[] _CalculateGrays(ToneProfile tone) => [
        LabColor.FromLch(tone.L1).ToCatColor("Gray1"),
        LabColor.FromLch(tone.L2).ToCatColor("Gray2"),
        LabColor.FromLch(tone.L3).ToCatColor("Gray3"),
        LabColor.FromLch(tone.L4).ToCatColor("Gray4"),
        LabColor.FromLch(tone.L5).ToCatColor("Gray5"),
        LabColor.FromLch(tone.L6).ToCatColor("Gray6"),
        LabColor.FromLch(tone.L7).ToCatColor("Gray7"),
        LabColor.FromLch(tone.L8).ToCatColor("Gray8"),
        LabColor.FromLch(tone.LWhite, alpha:tone.AHalfWhite).ToCatColor("HalfWhite", false),
        LabColor.FromLch(tone.LWhite, alpha:tone.ASemiWhite).ToCatColor("SemiWhite", false),
        LabColor.FromLch(tone.LWhite).ToCatColor("White", false),
        LabColor.FromLch(tone.LWhite, alpha:tone.ATransparent).ToCatColor("Transparent", false),
        LabColor.FromLch(tone.LBackground, alpha:tone.ABackground).ToCatColor("TransparentBackground", false),
        LabColor.FromLch(tone.LBackground).ToCatColor("Background", false),
        LabColor.FromLch(tone.LBackground, alpha:tone.AToolTip).ToCatColor("ToolTip", false),
        LabColor.FromLch(tone.L7, 0.25, 30, tone.AHalfTransparent).ToCatColor("RedBack", false),
        LabColor.FromLch(tone.LForeground).ToCatColor("Memory", false),
    ];

    private static CatColorResource[] _CalculateColors(ToneProfile tone, (int hue, double lightAdj, double chromaAdj) args) => [
        LabColor.FromLch(_AdjustLinear(tone.L1, args.lightAdj * 0.1), _AdjustLinear(tone.C1, args.chromaAdj * 0.25), args.hue).ToCatColor("1"),
        LabColor.FromLch(_AdjustLinear(tone.L2, args.lightAdj), _AdjustLinear(tone.C2, args.chromaAdj), args.hue).ToCatColor("2"),
        LabColor.FromLch(_AdjustLinear(tone.L3, args.lightAdj), _AdjustLinear(tone.C3, args.chromaAdj), args.hue).ToCatColor("3"),
        LabColor.FromLch(_AdjustLinear(tone.L4, args.lightAdj), _AdjustLinear(tone.C4, args.chromaAdj), args.hue).ToCatColor("4"),
        LabColor.FromLch(_AdjustLinear(tone.L5, args.lightAdj), _AdjustLinear(tone.C5, args.chromaAdj), args.hue).ToCatColor("5"),
        LabColor.FromLch(_AdjustLinear(tone.L6, args.lightAdj), _AdjustLinear(tone.C6, args.chromaAdj), args.hue).ToCatColor("6"),
        LabColor.FromLch(_AdjustLinear(tone.L7, args.lightAdj), _AdjustLinear(tone.C7, args.chromaAdj), args.hue).ToCatColor("7"),
        LabColor.FromLch(_AdjustLinear(tone.L8, args.lightAdj), _AdjustLinear(tone.C8, args.chromaAdj), args.hue).ToCatColor("8"),
        LabColor.FromLch(_AdjustLinear(tone.L8, args.lightAdj), _AdjustLinear(tone.C8, args.chromaAdj), args.hue, tone.ASemiTransparent).ToCatColor("SemiTransparent", false),
        LabColor.FromLch(_AdjustLinear(tone.L5, args.lightAdj), _AdjustLinear(tone.C5, args.chromaAdj), args.hue).ToCatColor("Bg0"),
        LabColor.FromLch(_AdjustLinear(tone.L7, args.lightAdj), _AdjustLinear(tone.C7, args.chromaAdj), args.hue, tone.ASemiWhite).ToCatColor("Bg1"),
    ];

    private static CatColorResource[] LightGrayCache { get => field ??= _CalculateGrays(ToneProfiles.Light); set; } = null!;

    private static CatColorResource[] DarkGrayCache { get => field ??= _CalculateGrays(ToneProfiles.Dark); set; } = null!;

    /// <summary>
    /// 清除灰度配色的计算缓存。
    /// </summary>
    public static void InvalidateGrayCache()
    {
        LightGrayCache = null!;
        DarkGrayCache = null!;
    }

    /// <summary>
    /// 应用灰度配色到 WPF 资源字典。
    /// </summary>
    public static void ApplyGrayResources()
    {
        var cache = IsDarkMode ? DarkGrayCache : LightGrayCache;
        foreach (var c in cache) c.Apply();
    }

    /// <summary>
    /// 应用彩色配色到 WPF 资源字典。
    /// </summary>
    public static void ApplyColorResources()
    {
        var colors = _CalculateColors(CurrentTone, GetCurrentThemeArgs());
        foreach (var c in colors) c.Apply();
    }
}
