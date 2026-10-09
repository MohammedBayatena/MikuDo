"""
Theme studio for MikuDo.

Each theme is written as semantic tokens over seed colours. Text and graphics
are then *fitted*: their OKLCH lightness is moved, hue kept, until every
background they sit on gives at least the required WCAG contrast. The result
is verified pair by pair, and only a theme that passes is written out as a
three-tier ResourceDictionary: primitive colours, semantic brushes that point
at them, and component tokens that point at the semantic ones.

Usage: python tools/theme_studio.py [--verbose] [--pairs] [--write MikuDo/Themes]

It writes a theme only when every pair passes; change colours here, not in the XAML.
"""
import math, sys, pathlib, re

# ── colour maths ────────────────────────────────────────────────────────────

def hex_to_rgba(h):
    h = h.lstrip('#')
    if len(h) == 6:
        return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255
    if len(h) == 8:  # #AARRGGBB, as WPF writes it
        return int(h[2:4], 16), int(h[4:6], 16), int(h[6:8], 16), int(h[0:2], 16)
    raise ValueError(h)

def rgb_to_hex(r, g, b):
    return '#%02x%02x%02x' % (r, g, b)

def lin(c):
    c = c / 255
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def delin(c):
    c = 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055
    return max(0, min(255, round(c * 255)))

def luminance(h):
    r, g, b, _ = hex_to_rgba(h)
    return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b)

def over(fg, bg):
    """fg composited over an opaque bg."""
    r, g, b, a = hex_to_rgba(fg)
    R, G, B, _ = hex_to_rgba(bg)
    t = a / 255
    return rgb_to_hex(round(r * t + R * (1 - t)), round(g * t + G * (1 - t)), round(b * t + B * (1 - t)))

def contrast(a, b):
    la, lb = luminance(a), luminance(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)

def to_oklab(h):
    r, g, b, _ = hex_to_rgba(h)
    r, g, b = lin(r), lin(g), lin(b)
    l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b
    m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b
    s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b
    l, m, s = l ** (1 / 3), m ** (1 / 3), s ** (1 / 3)
    return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s)

def from_oklab(L, a, b):
    l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
    m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
    s = (L - 0.0894841775 * a - 1.2914855480 * b) ** 3
    r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s
    g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s
    bb = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s
    inside = all(-1e-4 <= c <= 1 + 1e-4 for c in (r, g, bb))
    return rgb_to_hex(delin(max(0, min(1, r))), delin(max(0, min(1, g))), delin(max(0, min(1, bb)))), inside

def oklch(h):
    L, a, b = to_oklab(h)
    return L, math.hypot(a, b), math.degrees(math.atan2(b, a)) % 360

def from_oklch(L, C, H):
    while C > 0:
        out, ok = from_oklab(L, C * math.cos(math.radians(H)), C * math.sin(math.radians(H)))
        if ok:
            return out
        C *= 0.97
    return from_oklab(L, 0, 0)[0]

def fit(seed, backgrounds, ratio, margin=0.08):
    """Moves seed's lightness, hue kept, until it reaches ratio on every background."""
    target = ratio + margin
    if min(contrast(seed, bg) for bg in backgrounds) >= target:
        return seed
    L, C, H = oklch(seed)
    # Go away from the backgrounds: darker on light ones, lighter on dark ones.
    darker = sum(luminance(bg) for bg in backgrounds) / len(backgrounds) > 0.18
    step = -0.004 if darker else 0.004
    best = seed
    for _ in range(300):
        L = max(0.0, min(1.0, L + step))
        best = from_oklch(L, C, H)
        if min(contrast(best, bg) for bg in backgrounds) >= target:
            return best
    return best

def delta_e(a, b):
    """Distance in OKLab: how plainly two colours differ, hue and chroma included."""
    return math.dist(to_oklab(a), to_oklab(b))

def delta_l(a, b):
    return abs(to_oklab(a)[0] - to_oklab(b)[0])

def lstar(h):
    """CIE L*, perceived lightness on a 0-100 scale."""
    y = luminance(h)
    return 116 * (y ** (1 / 3) if y > 216 / 24389 else (24389 / 27 * y + 16) / 116) - 16

def shift_lightness(seed, base, low, high, darker):
    """Moves seed until its perceived lightness differs from base by between low and high (L* points)."""
    L, C, H = oklch(seed)
    step = -0.002 if darker else 0.002
    out = seed
    for _ in range(600):
        d = abs(lstar(out) - lstar(base))
        if low <= d <= high:
            return out
        if d > high:
            step = -step / 2
        L = max(0.0, min(1.0, L + step))
        out = from_oklch(L, C, H)
    return out

# ── naming the primitive tier ───────────────────────────────────────────────

FAMILIES = [(20, 'Red'), (55, 'Orange'), (75, 'Amber'), (100, 'Yellow'), (125, 'Lime'), (160, 'Green'),
            (195, 'Teal'), (225, 'Cyan'), (265, 'Blue'), (300, 'Violet'), (330, 'Magenta'), (350, 'Pink'), (361, 'Red')]

def primitive_name(h):
    r, g, b, a = hex_to_rgba(h)
    opaque = rgb_to_hex(r, g, b)
    L, C, H = oklch(opaque)
    family = 'Neutral' if C < 0.025 else next(name for limit, name in FAMILIES if H < limit)
    name = f'P.{family}.{round(L * 1000):03d}'
    if a != 255:
        name += f'.A{round(a / 255 * 100):02d}'
    return name

# ── what each token must reach, and on what ────────────────────────────────

TEXT_TOKENS = ['TextPrimary', 'TextSecondary', 'TextMuted', 'TextSubtle', 'TextFaint', 'TextGhost', 'TextGhostSoft']
TEXT_BACKGROUNDS = ['Surface', 'SurfaceMuted', 'SurfaceSubtle', 'Hover', 'Pressed', 'InlineAddBg', 'AccentSoft', 'AccentSoftAlt']
UI_BACKGROUNDS = ['Surface', 'SurfaceMuted', 'SurfaceSubtle']
STATUSES = ['Todo', 'Doing', 'Done']
PRIORITIES = ['High', 'Medium', 'Low', 'None']
LABELS = ['Green', 'Red', 'Amber', 'Blue', 'Violet', 'Cyan', 'Magenta', 'Olive']

# The empty bars of a priority glyph: they only have to be seen, not read, so the
# bar is lower than for text or controls; but it holds on every ground they sit on.
GHOST_BACKGROUNDS = ['Surface', 'SurfaceMuted', 'SurfaceSubtle', 'Hover']
GHOST_RATIO = 1.6

# Find's highlights: every match, and the one gone to. The page's text keeps its
# colour on them, so each must read under primary text; each must also stand out
# from the page, and the one gone to from the rest (distances in OKLab).
FIND_ON_PAGE = 0.08
FIND_APART = 0.06

# Perceived-lightness distance (L*) between a surface and its hover. The guide
# asks for 10-15%; a theme may set a gentler 'hover_shift' of its own.
HOVER_SHIFT = (10.5, 13)

def derive(theme):
    """Fills in fitted values, then returns (tokens, checks)."""
    t = dict(theme['tokens'])
    text = theme['text_ratio']
    ui = 3.0
    dark = theme['dark']

    # States first: everything that must read on them is fitted afterwards.
    # Hover moves perceived lightness 10-13 L* (darker on light themes, lighter
    # on dark ones) unless the theme sets its own 'hover_shift'; pressed goes a
    # further 6.
    low, high = theme.get('hover_shift', HOVER_SHIFT)
    t['Hover'] = shift_lightness(t['Hover'], t['Surface'], low, high, darker=not dark)
    t['Pressed'] = shift_lightness(t['Hover'], t['Surface'], low + 6, high + 6, darker=not dark)
    # The focus ring is two lines: the outer one stands out from the page, the
    # inner one from the control it surrounds. Which colour the inner line
    # needs depends on how dark the theme's filled controls are.
    t['FocusRingInner'] = t[theme.get('focus_inner', 'Surface')]

    def bgs(names):
        return [t[n] for n in names]

    for name in TEXT_TOKENS:
        t[name] = fit(t[name], bgs(TEXT_BACKGROUNDS), text)
    for name in ['AccentDark']:
        t[name] = fit(t[name], bgs(['Surface', 'SurfaceMuted', 'AccentSoft', 'AccentSoftAlt', 'AccentTint', 'CodeBg', 'QuoteBg', 'Hover']), text)
    t['Accent'] = fit(t['Accent'], bgs(['Surface', 'SurfaceMuted', 'AccentSoft', 'Hover']), text)
    for name in ['CheckBorder', 'InputBorder', 'Grip', 'Chevron', 'DashBorder', 'AccentTrack', 'ChartTrack', 'WaveIdle', 'AccentBullet']:
        t[name] = fit(t[name], bgs(UI_BACKGROUNDS), ui)
    t['FocusRing'] = fit(t['FocusRing'], bgs(['Surface', 'SurfaceMuted', 'Canvas', 'AccentSoft', 'Hover']), ui)
    t['Danger'] = fit(t['Danger'], bgs(['Surface', 'SurfaceMuted', 'DangerSoft', 'Hover']), text)
    t['DangerAccent'] = fit(t['DangerAccent'], bgs(['Surface', 'DangerSoft']), ui)
    t['Warning'] = fit(t['Warning'], bgs(['Surface', 'SurfaceMuted', 'WarningSoft', 'WarningTint']), text)
    # The bars of a priority not set, and the unset bars of one that is: faint on purpose,
    # but never lost on the hover colour a card or chip takes under the pointer.
    t.setdefault('PriorityGhost', t['Border'])
    t['PriorityGhost'] = fit(t['PriorityGhost'], bgs(GHOST_BACKGROUNDS), GHOST_RATIO)
    t['WarningFill'] = fit(t['WarningFill'], [t['OnWarning']], text)
    t['FindMatch'] = fit(t['FindMatch'], [t['TextPrimary']], text)
    t['FindActive'] = fit(t['FindActive'], [t['TextPrimary']], text)
    for fill in ['AccentFill', 'AccentGradientFrom', 'AccentGradientTo', 'BadgeActive', 'BadgeIdle']:
        t[fill] = fit(t[fill], [t['OnAccent']], text)
    t['Success'] = fit(t['Success'], [t['OnSuccess']], text)
    t['Success'] = fit(t['Success'], bgs(['Surface']), ui)
    t['LogoFrom'] = fit(t['LogoFrom'], [t['OnAccent']], ui)
    t['LogoTo'] = fit(t['LogoTo'], [t['OnAccent']], ui)
    t['AiStarFrom'] = fit(t['AiStarFrom'], bgs(['Surface', 'SurfaceMuted', 'AccentSoftAlt']), ui)
    t['AiStarTo'] = fit(t['AiStarTo'], bgs(['Surface', 'SurfaceMuted', 'AccentSoftAlt']), ui)
    for s in STATUSES:
        t[f'Status{s}'] = fit(t[f'Status{s}'], bgs(['Surface', 'SurfaceMuted', 'Hover', 'AccentSoft']) + [t[f'Status{s}Halo']], text)
        t[f'Status{s}Dot'] = fit(t[f'Status{s}Dot'], bgs(['Surface', 'SurfaceMuted']) + [t[f'Status{s}Halo']], ui)
    for p in PRIORITIES:
        t[f'Priority{p}'] = fit(t[f'Priority{p}'], bgs(['Surface', 'SurfaceMuted', 'Hover', 'AccentSoft']) + [t[f'Priority{p}Soft']], text)
    for lab in LABELS:
        t[f'Label{lab}'] = fit(t[f'Label{lab}'], [t[f'Label{lab}Soft']], text)
    t['WindowCloseHover'] = fit(t['WindowCloseHover'], [t['OnDanger']], ui)

    checks = []
    def need(fg, bg, ratio, what):
        f, b = t[fg], t[bg] if bg in t else bg
        if len(f) == 9:
            f = over(f, b)
        checks.append((what or f'{fg} on {bg}', f, b, contrast(f, b), ratio))

    for name in TEXT_TOKENS:
        for bg in TEXT_BACKGROUNDS:
            need(name, bg, text, None)
    for bg in ['Surface', 'SurfaceMuted', 'AccentSoft', 'AccentSoftAlt', 'AccentTint', 'CodeBg', 'QuoteBg', 'Hover']:
        need('AccentDark', bg, text, None)
    for bg in ['Surface', 'SurfaceMuted', 'AccentSoft', 'Hover']:
        need('Accent', bg, text, None)
    for fill in ['AccentFill', 'AccentGradientFrom', 'AccentGradientTo', 'BadgeActive', 'BadgeIdle']:
        need('OnAccent', fill, text, None)
    need('OnSuccess', 'Success', text, None)
    need('Success', 'Surface', ui, 'Success (saved dot) on Surface')
    for g in ['CheckBorder', 'InputBorder', 'Grip', 'Chevron', 'DashBorder', 'AccentTrack', 'ChartTrack', 'WaveIdle', 'AccentBullet']:
        for bg in UI_BACKGROUNDS:
            need(g, bg, ui, None)
    for bg in ['Surface', 'SurfaceMuted', 'Canvas', 'AccentSoft', 'Hover']:
        need('FocusRing', bg, ui, None)
    # The inner ring separates the outer one from whatever it surrounds.
    # Every control that is a solid fill; danger is only ever text and outlines.
    for element in ['AccentFill', 'AccentGradientFrom', 'AccentGradientTo', 'Success', 'BadgeActive', 'Surface', 'SurfaceMuted']:
        best = max(contrast(t['FocusRing'], t[element]), contrast(t['FocusRingInner'], t[element]))
        checks.append((f'focus ring (either line) against {element}', t['FocusRing'], t[element], best, ui))
    hover_shift = abs(lstar(t['Hover']) - lstar(t['Surface']))
    checks.append(('hover shift (L*)', t['Hover'], t['Surface'], hover_shift, theme.get('hover_shift', HOVER_SHIFT)[0] - 0.5))
    for bg in ['Surface', 'SurfaceMuted', 'DangerSoft', 'Hover']:
        need('Danger', bg, text, None)
    need('DangerAccent', 'DangerSoft', ui, None)
    for bg in ['Surface', 'SurfaceMuted', 'WarningSoft', 'WarningTint']:
        need('Warning', bg, text, None)
    for bg in GHOST_BACKGROUNDS:
        need('PriorityGhost', bg, GHOST_RATIO, None)
    need('OnWarning', 'WarningFill', text, None)
    for bg in ['FindMatch', 'FindActive']:
        need('TextPrimary', bg, text, None)
    for name in ['FindMatch', 'FindActive']:
        for bg in ['Surface', 'CodeBg']:
            checks.append((f'{name} apart from {bg} (OKLab)', t[name], t[bg], delta_e(t[name], t[bg]), FIND_ON_PAGE))
    checks.append(('FindActive apart from FindMatch (OKLab)', t['FindActive'], t['FindMatch'],
                   delta_e(t['FindActive'], t['FindMatch']), FIND_APART))
    need('OnAccent', 'LogoFrom', ui, 'logo icon on logo (from)')
    need('OnAccent', 'LogoTo', ui, 'logo icon on logo (to)')
    for st in ['AiStarFrom', 'AiStarTo']:
        need(st, 'Surface', ui, None)
    for s in STATUSES:
        for bg in ['Surface', 'SurfaceMuted', 'Hover', 'AccentSoft', f'Status{s}Halo']:
            need(f'Status{s}', bg, text, None)
        for bg in ['Surface', 'SurfaceMuted', f'Status{s}Halo']:
            need(f'Status{s}Dot', bg, ui, None)
    for p in PRIORITIES:
        for bg in ['Surface', 'SurfaceMuted', 'Hover', 'AccentSoft', f'Priority{p}Soft']:
            need(f'Priority{p}', bg, text, None)
    for lab in LABELS:
        need(f'Label{lab}', f'Label{lab}Soft', text, None)
    need('OnDanger', 'WindowCloseHover', ui, 'close icon on its hover')
    # The lightbox sits on its own dark scrim whatever the theme.
    scrim = over(t['GalleryScrim'], t['Surface'])
    for name in ['LightboxText', 'LightboxTextFaint']:
        checks.append((f'{name} on gallery scrim', over(t[name], scrim), scrim, contrast(over(t[name], scrim), scrim), text))
    checks.append(('LightboxIcon on gallery scrim', t['LightboxIcon'], scrim, contrast(t['LightboxIcon'], scrim), ui))
    # The tooltip is drawn inverted: surface-coloured text on a primary-text card.
    checks.append(('tooltip text (Surface on TextPrimary)', t['Surface'], t['TextPrimary'], contrast(t['Surface'], t['TextPrimary']), text))

    return t, checks

# ── the themes ──────────────────────────────────────────────────────────────

LIGHT_LABELS = {
    'LabelGreen': '#0f7a53', 'LabelGreenSoft': '#e7f8ef', 'LabelRed': '#be2a3f', 'LabelRedSoft': '#fdeef0',
    'LabelAmber': '#a35a00', 'LabelAmberSoft': '#fff3e4', 'LabelBlue': '#1f55d0', 'LabelBlueSoft': '#e4eeff',
    'LabelViolet': '#6d28d9', 'LabelVioletSoft': '#f1eaff', 'LabelCyan': '#0e6a83', 'LabelCyanSoft': '#e3f5f8',
    'LabelMagenta': '#9c2c93', 'LabelMagentaSoft': '#fbedf7', 'LabelOlive': '#4a7410', 'LabelOliveSoft': '#eef3e4',
}

SUN = {
    'name': 'Sun', 'dark': False, 'text_ratio': 4.5,
    # Whole cards take the hover colour on this theme, and at the guide's
    # 10 L* the grey reads heavy on white; 7 L* is still plainly visible.
    'hover_shift': (7, 8),
    'tokens': {
        'Canvas': '#e9edf3', 'Surface': '#fdfdfe', 'SurfaceMuted': '#f9f9fc', 'SurfaceSubtle': '#f5f6fa',
        'Hover': '#eef0f5', 'Divider': '#eceef3', 'DividerSoft': '#f1f2f6', 'Border': '#e3e6ed',
        'BorderSoft': '#e9ebf1', 'BorderFaint': '#e7e9f0', 'CheckBorder': '#c9ced8', 'DashBorder': '#cdd1da',
        'InputBorder': '#c4c9d4',
        'TextPrimary': '#1c1f27', 'TextSecondary': '#454c5b', 'TextMuted': '#5c6372', 'TextSubtle': '#646b7a',
        'TextFaint': '#6a7180', 'TextGhost': '#6d7483', 'TextGhostSoft': '#6f7686', 'Grip': '#b9bfca', 'Chevron': '#b2b8c4',
        'Accent': '#7c3aed', 'AccentFill': '#7c3aed', 'AccentDark': '#6527c9', 'AccentLight': '#8b5cf6',
        'AccentSoft': '#f4efff', 'AccentSoftAlt': '#f1edfd', 'AccentTint': '#ece3ff', 'AccentBorder': '#dccdfa',
        'AccentBorderStrong': '#c4aefa', 'AccentBullet': '#a68af2', 'AccentTrack': '#a9afbd',
        'BadgeActive': '#7c3aed', 'BadgeIdle': '#7b6aa8', 'ChartTrack': '#b9a6f5', 'WaveIdle': '#b8aaf0',
        'AccentGradientFrom': '#7c3aed', 'AccentGradientTo': '#6d28d9', 'LogoFrom': '#8b5cf6', 'LogoTo': '#6d28d9',
        'AiStarFrom': '#8b5cf6', 'AiStarTo': '#c026d3', 'AccentGlow': '#7c3aed',
        'OnAccent': '#fdfcff', 'OnSuccess': '#fbfefc', 'OnDanger': '#fffbfb',
        'Danger': '#b91c1c', 'DangerAccent': '#d3364a', 'DangerBorder': '#f3d3d6', 'DangerSoft': '#fdeef0',
        'Warning': '#9c5600', 'WarningSoft': '#fff3e4', 'WarningBorder': '#f3d7a8', 'WarningTint': '#fffaf2',
        'WarningFill': '#9c5600', 'OnWarning': '#fdfcff',
        'FindMatch': '#fde68a', 'FindActive': '#fbb86a',
        'Success': '#15803d', 'CodeBg': '#f2effd', 'QuoteBg': '#f8f6ff', 'InlineAddBg': '#fbfaff',
        'FocusRing': '#7c3aed', 'WindowCloseHover': '#d93a30',
        'Shadow': '#181d2d', 'Scrim': '#6b181d2d', 'GalleryScrim': '#d110131c',
        'LightboxText': '#b8ffffff', 'LightboxTextFaint': '#a6ffffff', 'LightboxIcon': '#f5f5f7', 'LightboxButton': '#24ffffff',
        'LightboxBorder': '#4dffffff', 'SwitchKnob': '#fdfdfe',
        'StatusTodo': '#a35a00', 'StatusTodoDot': '#d97706', 'StatusTodoHalo': '#fdf1d4',
        'StatusDoing': '#1f55d0', 'StatusDoingDot': '#2563eb', 'StatusDoingHalo': '#e2ecff',
        'StatusDone': '#0f7a53', 'StatusDoneDot': '#7c3aed', 'StatusDoneHalo': '#efe7ff',
        'PriorityHigh': '#be2a3f', 'PriorityHighSoft': '#fdeef0', 'PriorityMedium': '#a35a00', 'PriorityMediumSoft': '#fff3e4',
        'PriorityLow': '#1f55d0', 'PriorityLowSoft': '#e4eeff', 'PriorityNone': '#5c6372', 'PriorityNoneSoft': '#f1f3f7',
        **LIGHT_LABELS,
    },
}

MOON = {
    'name': 'Moon', 'dark': True, 'text_ratio': 4.5, 'focus_inner': 'Canvas',
    'tokens': {
        'Canvas': '#0d0f16', 'Surface': '#181b24', 'SurfaceMuted': '#14161e', 'SurfaceSubtle': '#1d212b',
        'Hover': '#262a37', 'Divider': '#242833', 'DividerSoft': '#20242e', 'Border': '#2c313d',
        'BorderSoft': '#262a36', 'BorderFaint': '#2a2f3b', 'CheckBorder': '#5a6274', 'DashBorder': '#5a6274',
        'InputBorder': '#5f6779',
        'TextPrimary': '#eef0f6', 'TextSecondary': '#c3c9d6', 'TextMuted': '#a7aebc', 'TextSubtle': '#9aa1b0',
        'TextFaint': '#959cab', 'TextGhost': '#9299a8', 'TextGhostSoft': '#9097a6', 'Grip': '#5d6577', 'Chevron': '#646c7e',
        'Accent': '#a78bfa', 'AccentFill': '#7c4ee8', 'AccentDark': '#c4b5fd', 'AccentLight': '#8b5cf6',
        'AccentSoft': '#221c33', 'AccentSoftAlt': '#251f38', 'AccentTint': '#2a2140', 'AccentBorder': '#463a6b',
        'AccentBorderStrong': '#5b4a8c', 'AccentBullet': '#8c76d6', 'AccentTrack': '#5f6779',
        'BadgeActive': '#7c4ee8', 'BadgeIdle': '#584a80', 'ChartTrack': '#6b5aa6', 'WaveIdle': '#6c618f',
        'AccentGradientFrom': '#7c4ee8', 'AccentGradientTo': '#6d28d9', 'LogoFrom': '#8b5cf6', 'LogoTo': '#6d28d9',
        'AiStarFrom': '#a78bfa', 'AiStarTo': '#e879f9', 'AccentGlow': '#7c3aed',
        'OnAccent': '#fbfaff', 'OnSuccess': '#0b1a12', 'OnDanger': '#fffbfb',
        'Danger': '#f87171', 'DangerAccent': '#f5899a', 'DangerBorder': '#4a2a30', 'DangerSoft': '#2e1a1f',
        'Warning': '#f0b45c', 'WarningSoft': '#33250f', 'WarningBorder': '#5e4520', 'WarningTint': '#1f1d1d',
        'WarningFill': '#e0a33e', 'OnWarning': '#1a1206',
        'FindMatch': '#5a4a12', 'FindActive': '#8a5410',
        'Success': '#34d17f', 'CodeBg': '#251f38', 'QuoteBg': '#1e1b2b', 'InlineAddBg': '#1c1a28',
        'FocusRing': '#b79cf7', 'WindowCloseHover': '#d93a30',
        'Shadow': '#05060a', 'Scrim': '#96000000', 'GalleryScrim': '#e00a0c12',
        'LightboxText': '#b8ffffff', 'LightboxTextFaint': '#a6ffffff', 'LightboxIcon': '#f5f5f7', 'LightboxButton': '#24ffffff',
        'LightboxBorder': '#4dffffff', 'SwitchKnob': '#f4f5f8',
        'StatusTodo': '#f0b45c', 'StatusTodoDot': '#e0a33e', 'StatusTodoHalo': '#33280f',
        'StatusDoing': '#7aa9f7', 'StatusDoingDot': '#5b8dee', 'StatusDoingHalo': '#15233d',
        'StatusDone': '#5ddba0', 'StatusDoneDot': '#a78bfa', 'StatusDoneHalo': '#251b3d',
        'PriorityHigh': '#f5899a', 'PriorityHighSoft': '#3a1a20', 'PriorityMedium': '#f0b45c', 'PriorityMediumSoft': '#33230f',
        'PriorityLow': '#7aa9f7', 'PriorityLowSoft': '#15233d', 'PriorityNone': '#a7aebc', 'PriorityNoneSoft': '#262a36',
        'LabelGreen': '#5ddba0', 'LabelGreenSoft': '#12321f', 'LabelRed': '#f5899a', 'LabelRedSoft': '#3a1a20',
        'LabelAmber': '#f0b45c', 'LabelAmberSoft': '#33230f', 'LabelBlue': '#7aa9f7', 'LabelBlueSoft': '#15233d',
        'LabelViolet': '#b79cf7', 'LabelVioletSoft': '#251b3d', 'LabelCyan': '#5fc4d6', 'LabelCyanSoft': '#122b30',
        'LabelMagenta': '#e79ade', 'LabelMagentaSoft': '#301a2d', 'LabelOlive': '#a7cc63', 'LabelOliveSoft': '#212a17',
    },
}

# Hatsune Miku: her teal (#39c5bb) and the deep teal of her ties, the cool grey
# of her outfit, a clean near-white, and the magenta-pink of her hair ribbons.
MIKU = {
    'name': 'MikuSpecial', 'dark': False, 'text_ratio': 4.5,
    # Her signature pink marks today on the chart.
    'components': {'ChartBarTodayBrush': 'BadgeActiveBrush'},
    'tokens': {
        'Canvas': '#e3f3f1', 'Surface': '#fafdfd', 'SurfaceMuted': '#f2f9f8', 'SurfaceSubtle': '#ebf6f4',
        'Hover': '#ddefec', 'Divider': '#dfeeec', 'DividerSoft': '#e8f4f2', 'Border': '#cfe5e2',
        'BorderSoft': '#d9ebe8', 'BorderFaint': '#d6e9e6', 'CheckBorder': '#9cc4bf', 'DashBorder': '#9fc8c3',
        'InputBorder': '#94bdb8',
        'TextPrimary': '#142629', 'TextSecondary': '#2a4044', 'TextMuted': '#3e5559', 'TextSubtle': '#475f63',
        'TextFaint': '#4d666a', 'TextGhost': '#506a6e', 'TextGhostSoft': '#526c70', 'Grip': '#93b9b4', 'Chevron': '#8cb4af',
        'Accent': '#0d8077', 'AccentFill': '#0f7f77', 'AccentDark': '#0a5f5a', 'AccentLight': '#39c5bb',
        'AccentSoft': '#ddf4f0', 'AccentSoftAlt': '#d4f0eb', 'AccentTint': '#c6ebe5', 'AccentBorder': '#a3dcd4',
        'AccentBorderStrong': '#66c6bb', 'AccentBullet': '#2fae a4'.replace(' ', ''), 'AccentTrack': '#88aeaa',
        'BadgeActive': '#d0186f', 'BadgeIdle': '#3f7f7a', 'ChartTrack': '#5cc0b6', 'WaveIdle': '#7cc9c1',
        'AccentGradientFrom': '#11908a', 'AccentGradientTo': '#0b6f69', 'LogoFrom': '#39c5bb', 'LogoTo': '#0f8a82',
        'AiStarFrom': '#18a79d', 'AiStarTo': '#e12885', 'AccentGlow': '#139c93',
        'OnAccent': '#fbfefe', 'OnSuccess': '#fbfefc', 'OnDanger': '#fffbfc',
        'Danger': '#b3125f', 'DangerAccent': '#e12885', 'DangerBorder': '#f6c7dc', 'DangerSoft': '#fdebf3',
        'Warning': '#9c5600', 'WarningSoft': '#fff3e4', 'WarningBorder': '#f3d7a8', 'WarningTint': '#fffaf2',
        'WarningFill': '#9c5600', 'OnWarning': '#fbfefe',
        'FindMatch': '#fde68a', 'FindActive': '#fbb86a',
        'Success': '#127a4f', 'CodeBg': '#e6f5f3', 'QuoteBg': '#f0f9f8', 'InlineAddBg': '#f6fbfb',
        'FocusRing': '#d0186f', 'WindowCloseHover': '#d81b60',
        'Shadow': '#0e3532', 'Scrim': '#6b0e2a28', 'GalleryScrim': '#d10b1c1b',
        'LightboxText': '#b8ffffff', 'LightboxTextFaint': '#a6ffffff', 'LightboxIcon': '#f4fbfa', 'LightboxButton': '#24ffffff',
        'LightboxBorder': '#4dffffff', 'SwitchKnob': '#fafdfd',
        'StatusTodo': '#a35a00', 'StatusTodoDot': '#d97706', 'StatusTodoHalo': '#fdf1d4',
        'StatusDoing': '#0b6aa8', 'StatusDoingDot': '#1287c9', 'StatusDoingHalo': '#dff0fa',
        'StatusDone': '#0a6b5f', 'StatusDoneDot': '#e12885', 'StatusDoneHalo': '#fde4ef',
        'PriorityHigh': '#b3125f', 'PriorityHighSoft': '#fdebf3', 'PriorityMedium': '#a35a00', 'PriorityMediumSoft': '#fff3e4',
        'PriorityLow': '#0b6aa8', 'PriorityLowSoft': '#e2f2fb', 'PriorityNone': '#3e5559', 'PriorityNoneSoft': '#e8f2f1',
        **LIGHT_LABELS,
        'LabelCyan': '#0a6b66', 'LabelCyanSoft': '#ddf4f0', 'LabelMagenta': '#b3125f', 'LabelMagentaSoft': '#fdebf3',
    },
}

# Creamy peach grounds, pastel pink accents, a deep rose for anything that must
# read, and warm plum-brown text.
PEACH = {
    'name': 'PastelPink', 'dark': False, 'text_ratio': 4.5,
    'tokens': {
        'Canvas': '#f9e5da', 'Surface': '#fffaf6', 'SurfaceMuted': '#fff3ec', 'SurfaceSubtle': '#fdede5',
        'Hover': '#f8e1d7', 'Divider': '#f4e1d8', 'DividerSoft': '#f8eae3', 'Border': '#efd4c8',
        'BorderSoft': '#f2dbd0', 'BorderFaint': '#f1d9ce', 'CheckBorder': '#d7a9a0', 'DashBorder': '#d9ada3',
        'InputBorder': '#d0a197',
        'TextPrimary': '#34212a', 'TextSecondary': '#523944', 'TextMuted': '#664c57', 'TextSubtle': '#6e535e',
        'TextFaint': '#735863', 'TextGhost': '#765b66', 'TextGhostSoft': '#785d68', 'Grip': '#d4a69d', 'Chevron': '#cfa097',
        'Accent': '#b8365f', 'AccentFill': '#c03e67', 'AccentDark': '#952a4d', 'AccentLight': '#f4a3bb',
        'AccentSoft': '#fde6ec', 'AccentSoftAlt': '#fbdee7', 'AccentTint': '#f8d2de', 'AccentBorder': '#f2bdcd',
        'AccentBorderStrong': '#e795af', 'AccentBullet': '#e06f93', 'AccentTrack': '#d0a3a5',
        'BadgeActive': '#c03e67', 'BadgeIdle': '#9a6573', 'ChartTrack': '#eb93ad', 'WaveIdle': '#ec9fb6',
        'AccentGradientFrom': '#c8456e', 'AccentGradientTo': '#ad3359', 'LogoFrom': '#e8708f', 'LogoTo': '#e9805f',
        'AiStarFrom': '#d4507b', 'AiStarTo': '#e7854f', 'AccentGlow': '#d4507b',
        'OnAccent': '#fffbfa', 'OnSuccess': '#fbfefc', 'OnDanger': '#fffbfa',
        'Danger': '#b0213d', 'DangerAccent': '#d63c5a', 'DangerBorder': '#f3c7cf', 'DangerSoft': '#fdebee',
        'Warning': '#9a4d08', 'WarningSoft': '#feeed9', 'WarningBorder': '#f1cfa6', 'WarningTint': '#fff6ee',
        'WarningFill': '#9a4d08', 'OnWarning': '#fffbfa',
        'FindMatch': '#fbe38e', 'FindActive': '#f9b474',
        'Success': '#17784c', 'CodeBg': '#fcebe6', 'QuoteBg': '#fff2ee', 'InlineAddBg': '#fff7f3',
        'FocusRing': '#b8365f', 'WindowCloseHover': '#d0344f',
        'Shadow': '#4a2430', 'Scrim': '#663a1d26', 'GalleryScrim': '#d11c1014',
        'LightboxText': '#b8ffffff', 'LightboxTextFaint': '#a6ffffff', 'LightboxIcon': '#fff7f5', 'LightboxButton': '#24ffffff',
        'LightboxBorder': '#4dffffff', 'SwitchKnob': '#fffaf6',
        'StatusTodo': '#a2510b', 'StatusTodoDot': '#e08a3c', 'StatusTodoHalo': '#feeed9',
        'StatusDoing': '#3657b8', 'StatusDoingDot': '#5a7be0', 'StatusDoingHalo': '#e8edfc',
        'StatusDone': '#17784c', 'StatusDoneDot': '#e06f93', 'StatusDoneHalo': '#fde6ec',
        'PriorityHigh': '#b0213d', 'PriorityHighSoft': '#fdebee', 'PriorityMedium': '#a2510b', 'PriorityMediumSoft': '#feeed9',
        'PriorityLow': '#3657b8', 'PriorityLowSoft': '#e8edfc', 'PriorityNone': '#664c57', 'PriorityNoneSoft': '#f8e9e3',
        **LIGHT_LABELS,
        'LabelRed': '#b0213d', 'LabelRedSoft': '#fdebee', 'LabelMagenta': '#a8306a', 'LabelMagentaSoft': '#fce8f1',
        'LabelViolet': '#7b3fb8', 'LabelVioletSoft': '#f3eafb',
    },
}

# Classic high contrast: near-black grounds, near-white text, yellow for
# everything that is chosen or acted on, strong borders everywhere.
CONTRAST = {
    'name': 'Contrast', 'dark': True, 'text_ratio': 7.0, 'focus_inner': 'Canvas',
    'tokens': {
        'Canvas': '#08080a', 'Surface': '#0e0e11', 'SurfaceMuted': '#0b0b0e', 'SurfaceSubtle': '#131317',
        'Hover': '#24242c', 'Divider': '#5e5e6a', 'DividerSoft': '#4c4c57', 'Border': '#9a9aa6',
        'BorderSoft': '#7c7c88', 'BorderFaint': '#7c7c88', 'CheckBorder': '#c8c8d2', 'DashBorder': '#b4b4c0',
        'InputBorder': '#c8c8d2',
        'TextPrimary': '#f7f7f8', 'TextSecondary': '#ededf0', 'TextMuted': '#e2e2e6', 'TextSubtle': '#d8d8de',
        'TextFaint': '#cfcfd6', 'TextGhost': '#c8c8cf', 'TextGhostSoft': '#c3c3ca', 'Grip': '#b4b4c0', 'Chevron': '#b4b4c0',
        'Accent': '#ffe14d', 'AccentFill': '#ffe14d', 'AccentDark': '#ffe766', 'AccentLight': '#fff099',
        'AccentSoft': '#2a2507', 'AccentSoftAlt': '#2f2908', 'AccentTint': '#383109', 'AccentBorder': '#bfa82f',
        'AccentBorderStrong': '#ffe14d', 'AccentBullet': '#ffe14d', 'AccentTrack': '#9a9aa6',
        'BadgeActive': '#ffe14d', 'BadgeIdle': '#d8d8de', 'ChartTrack': '#9a9aa6', 'WaveIdle': '#9a9aa6',
        'AccentGradientFrom': '#ffe14d', 'AccentGradientTo': '#ffd60a', 'LogoFrom': '#ffe14d', 'LogoTo': '#ffd60a',
        'AiStarFrom': '#ffe14d', 'AiStarTo': '#6ee7ff', 'AccentGlow': '#ffe14d',
        'OnAccent': '#0a0a0c', 'OnSuccess': '#0a0a0c', 'OnDanger': '#0a0a0c',
        'Danger': '#ff8a8a', 'DangerAccent': '#ff8a8a', 'DangerBorder': '#ff8a8a', 'DangerSoft': '#2e0f12',
        'Warning': '#ffd34d', 'WarningSoft': '#2e2708', 'WarningBorder': '#ffd34d', 'WarningTint': '#17150a',
        'WarningFill': '#ffd34d', 'OnWarning': '#0a0a0c',
        'FindMatch': '#4a3f00', 'FindActive': '#7a4600',
        'Success': '#6ef08f', 'CodeBg': '#1a1a20', 'QuoteBg': '#16161b', 'InlineAddBg': '#121216',
        'FocusRing': '#6ee7ff', 'WindowCloseHover': '#ff8a8a',
        'Shadow': '#000000', 'Scrim': '#c0000000', 'GalleryScrim': '#f0000000',
        'LightboxText': '#f2ffffff', 'LightboxTextFaint': '#d9ffffff', 'LightboxIcon': '#f7f7f8', 'LightboxButton': '#33ffffff',
        'LightboxBorder': '#99ffffff', 'SwitchKnob': '#0a0a0c',
        'StatusTodo': '#ffb347', 'StatusTodoDot': '#ffb347', 'StatusTodoHalo': '#2e2108',
        'StatusDoing': '#6ee7ff', 'StatusDoingDot': '#6ee7ff', 'StatusDoingHalo': '#08262e',
        'StatusDone': '#6ef08f', 'StatusDoneDot': '#6ef08f', 'StatusDoneHalo': '#0b2a13',
        'PriorityHigh': '#ff8a8a', 'PriorityHighSoft': '#2e0f12', 'PriorityMedium': '#ffd34d', 'PriorityMediumSoft': '#2e2708',
        'PriorityLow': '#7cc4ff', 'PriorityLowSoft': '#0c2138', 'PriorityNone': '#e2e2e6', 'PriorityNoneSoft': '#1f1f26',
        'LabelGreen': '#6ef08f', 'LabelGreenSoft': '#0b2a13', 'LabelRed': '#ff8a8a', 'LabelRedSoft': '#2e0f12',
        'LabelAmber': '#ffd34d', 'LabelAmberSoft': '#2e2708', 'LabelBlue': '#7cc4ff', 'LabelBlueSoft': '#0c2138',
        'LabelViolet': '#d0b8ff', 'LabelVioletSoft': '#1f1530', 'LabelCyan': '#6ee7ff', 'LabelCyanSoft': '#08262e',
        'LabelMagenta': '#ff9ce8', 'LabelMagentaSoft': '#2e0f29', 'LabelOlive': '#c8ea7a', 'LabelOliveSoft': '#1c260b',
    },
}

THEMES = [SUN, MOON, MIKU, PEACH, CONTRAST]

# ── emitting the ResourceDictionary ─────────────────────────────────────────

BRUSHES = {
    # semantic key: token (a solid brush over one primitive)
    'CanvasBrush': 'Canvas', 'SurfaceBrush': 'Surface', 'SurfaceMutedBrush': 'SurfaceMuted',
    'SurfaceSubtleBrush': 'SurfaceSubtle', 'HoverBrush': 'Hover', 'DividerBrush': 'Divider',
    'DividerSoftBrush': 'DividerSoft', 'BorderBrush': 'Border', 'BorderSoftBrush': 'BorderSoft',
    'BorderFaintBrush': 'BorderFaint', 'CheckBorderBrush': 'CheckBorder', 'DashBorderBrush': 'DashBorder',
    'InputBorderBrush': 'InputBorder',
    'TextPrimaryBrush': 'TextPrimary', 'TextSecondaryBrush': 'TextSecondary', 'TextMutedBrush': 'TextMuted',
    'TextSubtleBrush': 'TextSubtle', 'TextFaintBrush': 'TextFaint', 'TextGhostBrush': 'TextGhost',
    'TextGhostSoftBrush': 'TextGhostSoft', 'GripBrush': 'Grip', 'ChevronBrush': 'Chevron',
    'AccentBrush': 'Accent', 'AccentFillBrush': 'AccentFill', 'AccentDarkBrush': 'AccentDark',
    'AccentLightBrush': 'AccentLight', 'AccentSoftBrush': 'AccentSoft', 'AccentSoftAltBrush': 'AccentSoftAlt',
    'AccentTintBrush': 'AccentTint', 'AccentBorderBrush': 'AccentBorder', 'AccentBorderStrongBrush': 'AccentBorderStrong',
    'AccentBulletBrush': 'AccentBullet', 'AccentTrackBrush': 'AccentTrack', 'BadgeActiveBrush': 'BadgeActive',
    'BadgeIdleBrush': 'BadgeIdle', 'ChartTrackBrush': 'ChartTrack', 'WaveIdleBrush': 'WaveIdle',
    'OnAccentBrush': 'OnAccent', 'OnSuccessBrush': 'OnSuccess', 'OnDangerBrush': 'OnDanger',
    'DangerBrush': 'Danger', 'DangerAccentBrush': 'DangerAccent', 'DangerBorderBrush': 'DangerBorder',
    'DangerSoftBrush': 'DangerSoft',
    'WarningBrush': 'Warning', 'WarningSoftBrush': 'WarningSoft', 'WarningBorderBrush': 'WarningBorder',
    'WarningTintBrush': 'WarningTint', 'WarningFillBrush': 'WarningFill', 'OnWarningBrush': 'OnWarning',
    'PriorityGhostBrush': 'PriorityGhost',
    'FindMatchBrush': 'FindMatch', 'FindActiveBrush': 'FindActive',
    'SuccessBrush': 'Success', 'CodeBgBrush': 'CodeBg', 'QuoteBgBrush': 'QuoteBg',
    'InlineAddBgBrush': 'InlineAddBg', 'FocusRingBrush': 'FocusRing', 'FocusRingInnerBrush': 'FocusRingInner',
    'PressedBrush': 'Pressed',
    'ScrimBrush': 'Scrim', 'GalleryScrimBrush': 'GalleryScrim',
    'StatusTodoBrush': 'StatusTodo', 'StatusTodoDotBrush': 'StatusTodoDot', 'StatusTodoHaloBrush': 'StatusTodoHalo',
    'StatusDoingBrush': 'StatusDoing', 'StatusDoingDotBrush': 'StatusDoingDot', 'StatusDoingHaloBrush': 'StatusDoingHalo',
    'StatusDoneBrush': 'StatusDone', 'StatusDoneDotBrush': 'StatusDoneDot', 'StatusDoneHaloBrush': 'StatusDoneHalo',
    **{f'Priority{p}Brush': f'Priority{p}' for p in PRIORITIES},
    **{f'Priority{p}SoftBrush': f'Priority{p}Soft' for p in PRIORITIES},
    **{f'Label{l}Brush': f'Label{l}' for l in LABELS},
    **{f'Label{l}SoftBrush': f'Label{l}Soft' for l in LABELS},
}

GRADIENTS = {
    'AccentGradientBrush': ('AccentGradientFrom', 'AccentGradientTo', '0.15,0', '0.85,1'),
    'LogoGradientBrush': ('LogoFrom', 'LogoTo', '0.15,0', '0.85,1'),
    'AiStarBrush': ('AiStarFrom', 'AiStarTo', '0,0', '1,1'),
}

COLORS = {'ShadowColor': 'Shadow', 'AccentGlowColor': 'AccentGlow'}

# Component tokens: what particular controls paint with, named for the control.
COMPONENTS = {
    'ButtonPrimaryForegroundBrush': 'OnAccentBrush',
    'ButtonSuccessForegroundBrush': 'OnSuccessBrush',
    'BadgeForegroundBrush': 'OnAccentBrush',
    'SwitchTrackOffBrush': 'AccentTrackBrush',
    'SwitchTrackOnBrush': 'AccentFillBrush',
    'ChartBarBrush': 'ChartTrackBrush',
    'ChartBarTodayBrush': 'AccentFillBrush',
    'WindowCloseHoverForegroundBrush': 'OnDangerBrush',
    'TipLabelBrush': 'AccentDarkBrush',
    # aliases the confirm dialog and the preview bridge read by name
    'CardBrush': 'SurfaceBrush', 'CardHoverBrush': 'HoverBrush', 'ForegroundBrush': 'TextPrimaryBrush',
    'SubtleBrush': 'TextSecondaryBrush', 'MutedBrush': 'TextSubtleBrush', 'BackgroundBrush': 'SurfaceMutedBrush',
    'InputBrush': 'SurfaceBrush',
}

# Component tokens over their own primitives, for surfaces that sit outside any theme surface.
COMPONENT_BRUSHES = {
    'WindowCloseHoverBrush': 'WindowCloseHover', 'SwitchKnobBrush': 'SwitchKnob',
    'LightboxTextBrush': 'LightboxText', 'LightboxTextFaintBrush': 'LightboxTextFaint',
    'LightboxIconBrush': 'LightboxIcon', 'LightboxButtonBrush': 'LightboxButton', 'LightboxBorderBrush': 'LightboxBorder',
}

HEADER = '''<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=System.Runtime"
                    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options"
                    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                    mc:Ignorable="po">
'''

def emit(theme, t, title):
    used = {}
    def prim(token):
        h = t[token].lower()
        name = primitive_name(h)
        base, n = name, 0
        while name in used and used[name] != h:
            n += 1
            name = f'{base}.{chr(ord("a") + n - 1)}'
        used[name] = h
        return name

    sem_lines, grad_lines, color_lines, comp_lines = [], [], [], []
    for key, token in BRUSHES.items():
        sem_lines.append(f'    <SolidColorBrush x:Key="{key}" po:Freeze="True" Color="{{StaticResource {prim(token)}}}"/>')
    for key, (a, b, start, end) in GRADIENTS.items():
        grad_lines.append(f'    <LinearGradientBrush x:Key="{key}" po:Freeze="True" StartPoint="{start}" EndPoint="{end}">\n'
                          f'        <GradientStop Color="{{StaticResource {prim(a)}}}" Offset="0"/>\n'
                          f'        <GradientStop Color="{{StaticResource {prim(b)}}}" Offset="1"/>\n'
                          f'    </LinearGradientBrush>')
    # A Color cannot point at another Color without a <StaticResource> alias
    # element, and WPF resolves those aliases unreliably inside a dictionary;
    # so these are written out, each naming the primitive it carries.
    for key, token in COLORS.items():
        color_lines.append(f'    <Color x:Key="{key}">{t[token].lower()}</Color> <!-- {prim(token)} -->')
    for key, token in COMPONENT_BRUSHES.items():
        comp_lines.append(f'    <SolidColorBrush x:Key="{key}" po:Freeze="True" Color="{{StaticResource {prim(token)}}}"/>')
    # Each component token is its own brush over the same primitive as the
    # semantic token it stands for: no alias elements, which WPF can resolve
    # to the wrong resource.
    for key, target in {**COMPONENTS, **theme.get('components', {})}.items():
        comp_lines.append(f'    <SolidColorBrush x:Key="{key}" po:Freeze="True" Color="{{StaticResource {prim(BRUSHES[target])}}}"/>'
                          f' <!-- = {target} -->')

    def argb(h):
        r, g, b, a = hex_to_rgba(h)
        return '#%02x%02x%02x%02x' % (a, r, g, b) if a != 255 else rgb_to_hex(r, g, b)

    prim_lines = [f'    <Color x:Key="{name}">{argb(h)}</Color>' for name, h in sorted(used.items())]
    kind = 'dark' if theme['dark'] else 'light'
    return (HEADER +
            f'\n    <!-- {title}: a {kind} theme. Written by tools/theme_studio.py, which checked every\n'
            f'         text and control colour below against WCAG contrast before writing it. -->\n'
            f'    <sys:Boolean x:Key="ThemeIsDark">{"True" if theme["dark"] else "False"}</sys:Boolean>\n'
            f'    <sys:Double x:Key="DisabledOpacity">0.5</sys:Double>\n'
            '\n    <!-- ═══ Tier 1: primitive colours. Nothing outside this file names them. ═══ -->\n' +
            '\n'.join(prim_lines) +
            '\n\n    <!-- ═══ Tier 2: semantic tokens, what a colour is for ═══ -->\n' +
            '\n'.join(sem_lines) + '\n' + '\n'.join(grad_lines) + '\n' + '\n'.join(color_lines) +
            '\n\n    <!-- ═══ Tier 3: component tokens, what one control paints with ═══ -->\n' +
            '\n'.join(comp_lines) + '\n\n</ResourceDictionary>\n')

TITLES = {'Sun': 'Sun', 'Moon': 'Moon', 'MikuSpecial': 'Miku Special', 'PastelPink': 'Pastel Pink', 'Contrast': 'Contrast'}

def main():
    write = '--write' in sys.argv
    out_dir = pathlib.Path(sys.argv[sys.argv.index('--write') + 1]) if write else None
    failures = 0
    report = []
    for theme in THEMES:
        t, checks = derive(theme)
        bad = [c for c in checks if c[3] < c[4]]
        failures += len(bad)
        worst_text = min(c[3] for c in checks if any(c[0].startswith(n) for n in TEXT_TOKENS))
        hover = abs(lstar(t['Hover']) - lstar(t['Surface']))
        report.append(f"{theme['name']:12} {len(checks)} pairs, {len(bad)} below target; "
                      f"weakest text {worst_text:.2f}:1; hover shift {hover:.1f} L*")
        for c in bad:
            report.append(f'    FAIL {c[0]}: {c[1]} on {c[2]} = {c[3]:.2f} (needs {c[4]})')
        changed = {k: (theme['tokens'][k], v) for k, v in t.items() if theme['tokens'].get(k, v).lower() != v.lower()}
        if '--verbose' in sys.argv and changed:
            report.append('    fitted: ' + ', '.join(f'{k} {a}->{b}' for k, (a, b) in changed.items()))
        if write and not bad:
            (out_dir / f"{theme['name']}.xaml").write_text(emit(theme, t, TITLES[theme['name']]), encoding='utf-8')
        if '--pairs' in sys.argv:
            for c in checks:
                report.append(f'    {c[3]:5.2f} >= {c[4]:<4} {c[0]}')
    print('\n'.join(report))
    print('ALL PAIRS PASS' if failures == 0 else f'{failures} PAIRS BELOW TARGET')
    return 1 if failures else 0

if __name__ == '__main__':
    sys.exit(main())
