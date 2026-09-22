# NvimManager — Glass UI Design

A cozy, feminine dark-frosted-glass world for a Neovim plugin manager. The redesign re-skins the whole app: custom chrome, ambient depth, translucent "glass" panels over warm plum-mauve light, and pastel rose accents — no emoji-as-icons, authoiced vector glyphs in one stroke weight.

## Direction
- **Dark frosted glass** everywhere (full re-skin, not highlights).
- **Cozy, feminine and cute**: warm blush rose + lavender pastels, soft rounded forms, gentle glows, warm near-black ground instead of pure black.
- Everything is a *glass layer* over an ambient gradient; nothing sits on flat void.

## Canonical files
- `src/NvimManager.App/Themes/Glass.axaml` — palette, brushes, effects, authored icons (ResourceDictionary, merged via `MergeResourceInclude`).
- `src/NvimManager.App/Themes/GlassStyles.axaml` — global control styles (included via `StyleInclude` after `FluentTheme`).
- Views in `src/NvimManager.App/Views/` use `Classes="glass"` / `"inset"` / `"pill"` + `accent` / `soft` buttons.

## Palette (ground truth lives in Glass.axaml)
| Token | Purpose |
|---|---|
| `Ground.Base #1A161D` | warm dark plum-mauve ground, never black |
| `Brush.Ambient` | vertical plum gradient the whole app sits on |
| `Brush.Glow.Top/Bottom` | soft rose / lavender radial glows at the corners |
| `Glass.Fill` (`#11FFFFFF` … `#26FFFFFF`) | layered frosted fills (+ strong, hover, inset states) |
| `Glass.Border` (`#2AFFFFFF`) | white hairlines at low alpha |
| `Accent.Rose #F4A7C3` | primary accent (rose) |
| `Accent.Lavender #C3B1E1` | secondary (focus, category hints) |
| `Accent.Honey #F0C07A` | warnings / update-available |
| `Accent.Mint #9CD1AE` / `Accent.Coral #F0938B` | ok / destructive |
| `Fg.*` | warm foreground scale, not gray |

Contrast: text uses `Fg.Primary` or `Fg.Secondary` ≥ 4.5:1 on ambient ground; low-opacity accents are never used for legible text.

## Icons
- Authoiced `StreamGeometry` (16×16 viewbox), one consistent stroke weight (~1.4–1.6).
- Keys: `Icon.Star`, `Icon.Heart`, `Icon.Check`, `Icon.BoxChecked`, `Icon.Sliders`, `Icon.Min/Max/Close`.
- No emoji or dingbat glyphs as icons. Star counts render the `Icon.Star` path, not `★`.

## Components
- **Window**: `TransparencyLevelHint="AcrylicBlur"`, transparent background, custom chrome (`ExtendClientArea*` + `NoChrome`), 40px title bar that drags (`BeginMoveDrag`) and double-click maximizes; min/max/close caption buttons (close → coral on hover).
- **Ambient scene**: full-window ambient gradient + two glows `IsHitTestVisible="False"`.
- **Sidebar**: frosted panel, rose-tinted selected pill, authored icon + title per nav item; footer caption.
- **Cards** (`Border.glass`): frosted panels for store rows, installed rows; hover lift via `ListBoxItem` hover fill.
- **Buttons**: cozy pills — `accent` (rose→lavender gradient, soft glow: primary action), `soft` (quiet outline), default (glass), `icon`, `caption`.
- **Chips / toggles**: `ToggleButton` checked → accent gradient.
- **Inputs**: frosted inset wells, rose caret, soft lavender selection, focus ring via `:focus-within`; watermark auto-hides.
- **Status**: frosted pills ("Installed", "Update available" in honey), progress bars with pastel rose gradient and rounded track.
- **Motion**: one authored transition — `CrossFade` on page changes (220 ms).

## States covered
hover, pressed, focus-visible (lavender ring), disabled (0.45 opacity), selected, busy (`IsBusy` → progress bar), empty/loading flows rely on existing VMs (`IsLoading`, `StatusText`).

## Runtime notes for future edits
- Resource lookup prefers `DynamicResource` across views/styles (app-merged cupboards are not visible to `StaticResource` inside styles at populate time).
- `NullableIntConverter` lives directly in `Application.Resources`; referenced with `StaticResource` inside bindings (`{Binding Priority, Converter={StaticResource NullableIntConverter}}`).
- Prefer explicit path names in templates (`x:Name="ItemRoot"` etc.) and cast styles like `Button.accent:pointerover`.
- `Path` uses `StrokeJoin` (not `StrokeLineJoin`); `TextBox` in Avalonia 11 has no `WatermarkVisible`/scrollbar-visibility instance props (see template workaround).