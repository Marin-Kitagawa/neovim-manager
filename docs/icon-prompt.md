# App Icon Generation Prompt (NvimManager)

Primary prompt for image generators (Midjourney / DALL-E / Imagen / SDXL):

---

A cozy, feminine app icon for a Neovim plugin manager called NvimManager.
Modern glassmorphism style, macOS Big Sur / iOS style rounded-square icon
(superellipse), filling the frame edge to edge.

**Background:** warm near-black plum-mauve ground (#1A161D) with a soft
vertical ambient gradient and two gentle radial glows in the top corners —
a blush rose glow and a lavender glow — like light behind frosted glass.

**Subject:** a small rounded terminal window made of translucent frosted
glass (white at ~10–15% opacity, hairline white border at ~20% opacity,
soft inner glow), tilted very slightly for depth. Inside the terminal,
a minimal command-line prompt symbol ">" and a tiny cursor block glowing
in pastel rose (#F4A7C3). Growing out of the terminal's top edge is a cute
two-leaf sprout in soft mint (#9CD1AE) with a rounded, chubby, friendly
silhouette — the sprout is the visual anchor, centered, taking ~40% of
the icon's height.

**Accents:** a small pastel honey (#F0C07A) sparkle or heart near the
sprout; one or two tiny lavender (#C3B1E1) dots floating like soft bokeh.

**Rendering:** soft studio lighting, subtle depth-of-field, gentle outer
drop shadow, smooth vector-like shapes with slightly rounded corners
everywhere, high detail but clean and uncluttered, premium cozy desktop-
app aesthetic. Matte glass, no harsh reflections, no neon.

**Constraints:** no text, no letters, no watermark, no emoji-style flat
clipart, no pure black, no neon glow, no photorealistic wood/stone
textures. Square 1:1, 1024×1024, centered composition, safe margins of
~7% on all sides.

---

## Short variant (Midjourney-style one-liner)

> glassmorphism app icon, rounded square, dark plum-mauve background
> #1A161D with soft rose and lavender radial glows, cute two-leaf sprout
> in pastel mint growing from a small frosted-glass terminal window with
> a glowing rose ">" prompt cursor, honey accent sparkle, soft studio
> light, subtle bokeh, cozy feminine premium desktop app aesthetic,
> vector-clean, centered, no text, 1024x1024 --v 6 --style raw

## Negative prompt (for SD/SDXL)

> text, letters, words, watermark, emoji, flat clipart, neon, pure black
> background, harsh reflections, busy background, photorealistic skin,
> low contrast, blur, distorted geometry, clutter

## Notes

- Palette must match `src/NvimManager.App/Themes/Themes.axaml`:
  ground #1A161D, rose #F4A7C3, lavender #C3B1E1, honey #F0C07A, mint #9CD1AE.
- After generating, round the corners yourself (mask to a superellipse)
  so Windows/Linux/macOS show it consistently; export 256/64/48/32/16 px
  `.ico` for Windows and 512/256 px `.png` for Linux `.desktop` entries.
