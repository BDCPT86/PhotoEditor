# UI Design System

The rules behind FrameForge's interface. The tokens and control styles live in
[`src/FrameForge.App/Themes/Theme.xaml`](../src/FrameForge.App/Themes/Theme.xaml); this document says why they are what
they are and how to use them. Change both together.

## Direction: a viewing booth
FrameForge is where photographs are judged, so the interface behaves like a neutral viewing surround:

- **The photograph is the hero.** Chrome steps back in tone; nothing competes with the image for colour or contrast.
- **Hue-free greys.** Every surface is a true neutral (R = G = B). A tinted UI (the usual blue-grey "dark mode") shifts
  how colour in a photo is perceived; a neutral one does not.
- **Tone, not boxes.** Regions are separated by tonal steps and spacing. Hairlines appear only where two regions of the
  same tone meet. No outlined cards, no nested containers.
- **One accent, used sparingly.** Cyanotype blue marks actions, keyboard focus and "this filter is on". Decision colours
  (keep, review, reject) appear only where a decision is shown.
- **The signature: a print mat.** Selection is a warm-white frame around the photo, like a mat cut around a print. It
  reads on any image and does not rely on colour. The same mat marks the active panel in Compare and the focused Auto
  option.
- **Restraint.** No gradients, glass, glow, oversized cards or decorative AI imagery. Corners stay small (2–6 px);
  photos are never rounded into "cards".

## Layout
```
┌──────────────┬───────────────────────────────────────────────────────────────┐
│ FrameForge   │ All photos  1,010          Import ▾  Analyze  Compare  Presets  Export  ? │  header (chrome)
│              │ Rating [Any 1★ …]  Label [Any ● ● ● ●]  Analysis [Any ▾]   Sort [Folder ▾] [▫▫▫] │  refine row (chrome)
│ Library      ├───────────────────────────────────────────────────────────────┤
│  All photos  │                                                               │
│  Undecided   │          photo grid / viewer / compare (canvas)               │ side panel
│  Kept        │                                                               │ (panel tone,
│  To review   │                                                               │  pushes the
│  Rejected    │                                                               │  grid aside)
│  Last import │                                                               │
│ Folders      │                                                               │
│  All folders │                                                               │
│  Randoms     ├───────────────────────────────────────────────────────────────┤
│ Settings     │ status · progress · Cancel                  4 keep · 0 review · … │  footer (chrome)
│ About        │                                                               │
└──────────────┴───────────────────────────────────────────────────────────────┘
```
- **Sidebar (228 px, chrome):** wordmark; **Library** views with live counts; **Folders** as an indented tree (a folder
  includes its subfolders); **Settings** and **About** at the foot. Views and the folder combine. Views that would be
  empty by definition (Problems with no problems, Last import before any import) are not shown.
- **Header (chrome):** the view title (page-title type) and the number of photos shown; the primary actions as quiet
  icon + label buttons. Import's options sit behind its chevron.
- **Refine row (chrome):** rating and label as segmented controls, analysis findings as one drop-down (eight choices
  would be a wall of chips), then sort and thumbnail size on the right. Active filters turn their caption accent.
- **Canvas:** the grid, viewer and compare. Darkest tone, so photos sit on the quietest ground.
- **Footer (chrome):** import and analysis progress with Cancel, the last result (⚠ + error colour for warnings), and
  library totals.
- **Side panels** (Export, Presets, Analysis details, Shortcuts): a column of their own at panel tone, so they push
  content aside instead of covering it. Header = panel title + icon close button.
- **Viewer and compare:** a 48 px chrome header (back to grid, file name, position; decision, zoom, Develop on the
  right), the canvas, and a 32 px chrome footer for metadata and key hints. Develop is a 320 px panel on the right.

## Colour tokens
| Token | Value | Use |
|---|---|---|
| `Color.Canvas` | `#121212` | Grid, viewer, compare, pages |
| `Color.Chrome` | `#191919` | Sidebar, header, refine row, footers |
| `Color.Panel` | `#1D1D1D` | Side panels, Develop, Auto tray, selected tile backing |
| `Color.Control` | `#2A2A2A` | Secondary buttons, key caps, callouts |
| `Color.Control.Hover` / `.Pressed` | `#333333` / `#232323` | Button states, selected list rows, slider rail |
| `Color.Field` | `#141414` | Text boxes, drop-downs, lists, segment tracks (recessed) |
| `Color.Popup` | `#262626` | Menus, drop-down lists, tooltips |
| `Color.Selected` | `#2B2B2B` | Selected sidebar row |
| `Color.Hairline` | `#292929` | Region seams, section separators |
| `Color.Stroke` / `.Hover` | `#383838` / `#4A4A4A` | Field outlines, popup edges |
| `Color.Text.Primary` | `#ECECEC` | Content, active labels |
| `Color.Text.Secondary` | `#A6A6A6` | Supporting text, inactive navigation |
| `Color.Text.Tertiary` | `#878787` | Counts, captions, file names, key hints (≥ 4.5:1 on canvas and chrome) |
| `Color.Text.Disabled` | `#5E5E5E` | Disabled only |
| `Color.Accent` (+ `.Hover`, `.Pressed`) | `#5AA2D8` | Primary buttons, focus, active filters, checked controls |
| `Color.Accent.Subtle` | `#5AA2D8` at 18 % | Checked toggle fill, "Edited" chip |
| `Color.OnAccent` | `#0B1722` | Text and marks on accent fills |
| `Color.Mat` | `#EAE6DF` | Selection frame |
| `Color.Status.Error` / `.Success` / `.Warning` | `#E5695F` / `#73B777` / `#DBA84A` | Messages, always with ⚠ or ✓ |
| `Brush.Decision.Keep` / `.Review` / `.Reject` | `#73B777` / `#DBA84A` / `#E5695F` | ✓ ? ✕ marks only |
| `Brush.Label.*`, `Brush.Rating` | — | Label swatches (with a letter), stars |
| `Brush.HoverTint` | white at 7 % | Hover over any tone |
| `Brush.Scrim` | `#121212` at 78 % | Backing for marks drawn on photos |

The older names (`Brush.Surface.Sunken/Base/Raised/Hover`, `Brush.Border`) still resolve and map onto these steps;
use the new names in new work.

## Typography
One family: **Segoe UI Variable** (Text and Display optical sizes) on Windows 11, falling back to Segoe UI. Hierarchy
comes from size and weight, never from a second display face. Numbers that line up (counts, slider values, progress)
use tabular figures.

| Role | Style key | Size / weight |
|---|---|---|
| Page title, view name | `Text.PageTitle` | 20 SemiBold, Display |
| Panel title | `Text.PanelTitle` | 15 SemiBold |
| Section heading (Settings) | `SettingsSection` (on `Text.SectionLabel`) | 14 SemiBold |
| Section heading (panels), Develop section headers | `Text.SectionLabel` | 13 SemiBold |
| Navigation, buttons, body | (default) | 13 Regular |
| Hints, explanations | `Text.Hint` | 12, secondary, 17 px line height |
| Status | `Text.Status` | 12, secondary, trimmed |
| Counts, key hints | `Text.Count` | 12, tertiary, tabular |
| Sidebar group heading ("Library", "Folders") | `Text.SidebarHeading` | 11 SemiBold, tertiary, sentence case |
| File names on tiles | — | 11, tertiary (primary when selected) |
| Marks on photos | `Text.AnalysisBadge` | 10.5 |

Sentence case everywhere. No tracked all-caps eyebrows.

## Spacing, shape, depth
- **4 px grid:** `Space.1`–`Space.6` = 4, 8, 12, 16, 24, 32. Tight inside a group (4–8), generous between groups
  (24–36), more space above a heading than below it.
- **Radius:** `Radius.Control` 4 (buttons, fields), `Radius.Popup` 6 (menus, callouts, groups), 2–3 for tiles and
  marks. Nothing is pill-shaped except the round decision chip.
- **Depth:** only things that float (menus, drop-downs, tooltips) have a shadow: `Shadow.Popup`, offset 4 px down, soft
  18 px blur, never a coloured halo.

## Icons
The Windows system icon font (`Font.Icon` = Segoe Fluent Icons, falling back to Segoe MDL2 Assets): one consistent
line style, no bundled assets. 14 px in toolbars, 12 px inline, 10 px for chevrons. Icons pair with a label except in
icon buttons, which always have a tooltip and an automation name. Decision marks (✓ ? ✕), stars and analysis marks
(◆ ≈ ◐ ▣ =) are text glyphs so they share the type's weight.

## Components and states
Every interactive control has hover, pressed, keyboard focus and disabled states.

| Component | Style | Look | States |
|---|---|---|---|
| Secondary button (default) | `Button` | Control fill, no outline | hover lighter, pressed darker, disabled dim |
| Primary button | `Button.Primary` | Accent fill, on-accent SemiBold | hover/pressed accent steps; disabled falls back to control |
| Quiet button | `Button.Quiet` | No fill until hovered | toolbar actions, back buttons |
| Icon button | `Button.Icon` | 30 × 30 quiet, icon font | tooltip required |
| Link | `Button.Link` | Accent text, underline on hover | Cancel, Retry, Clear refinements |
| Toggle | `ToggleButton`, `IconToggle` | Like secondary | checked = accent-subtle fill + accent text |
| Segmented control | `SegmentGroup` + `Segment` | Recessed track, borderless options | chosen = raised pill, primary text |
| Text box | `TextBox` | Field fill, 1 px stroke | hover stroke lighter, focus stroke accent (no width change) |
| Drop-down | `ComboBox`, `ComboBoxItem` | Like a text box with a chevron; list on a popup | highlighted row raised; chosen row ✓ in accent |
| Check box, radio | `CheckBox`, `RadioButton` | 16 px, field fill | checked = accent fill with on-accent mark; focus = accent ring |
| Slider | `Slider` | 3 px neutral rail, 12 px light thumb | hover grows thumb, drag turns it accent, keyboard focus turns the rail accent. No fill: most edit sliders centre on zero |
| Tooltip | `ToolTip` | Popup tone, 12 px, max 360 px | — |
| Context menu | `ContextMenu`, `MenuItem` | Popup tone, rows with key hints on the right, submenus | highlighted row raised, ✓ for checked items |
| List rows | `ListBoxItem` | Transparent | hover tint, selected raised + SemiBold, focus accent outline |
| Navigation row | `NavigationListItem`, `NavigationButton` | Transparent | hover tint, selected = `Selected` tone + primary text, focus accent outline; dimmed while another page is shown |
| Expander section | `PanelSection` | Header row with chevron, hairline above | — |
| Side panel | `SidePanel` | Panel tone, hairline seam on the left | — |
| Grouped block | `Surface.Group` | Chrome tone, 6 px radius | AI model entries |
| Callout | `Surface.Callout` | Control tone, 6 px radius | inline confirmations (export into the library, model consent) |
| Progress | `ProgressBar` | 3 px, accent on control | indeterminate while scanning |

Dialogs: routine work never opens a modal. Confirmations are inline callouts in the panel where the action happens;
the only system dialogs are the folder and file pickers and the fatal start-up error box.

## Photo grid
- **Cells** are uniform (three densities: 136 × 128, 196 × 182, 272 × 248 DIP, stretched evenly to the width). Each
  photo is fitted, not cropped, and centred; its marks sit on the photo's own corners, not the letterbox.
- **Selection** = the print mat (2 px warm white) around the cell with the panel tone behind it. **Keyboard focus** adds
  a 1 px accent ring outside the mat, so the focused photo stands out within a multi-selection. **Hover** lifts the cell
  one tone. No transitions: culling never waits on animation.
- **Decision** = a ✓ ? ✕ chip on a scrim, top right, in the decision colour. Rejected photos also dim to 35 %.
- **Judgement marks** (always shown, top left, on a scrim): AI pick, ◆ sharpest, ≈ blur, = dup, ☺ soft.
  **Informational marks** (clipping ◐, group size ▣ n, faces ☺ n) appear only on hover or selection, so scanning stays
  quiet. Every mark has a tooltip; **I** gives the numbers.
- **Caption:** ✎ (edited) and the file name on the left, stars and the label swatch (with its letter) on the right. The
  smallest density hides the name but keeps ✎, stars and label.
- **Loading:** a cell without its thumbnail shows a flat placeholder of the same size (no spinners in the grid).
- **Empty states** say what to do and offer the action: an empty library offers **Import folder…** (primary) and
  mentions drag and drop; an empty view offers **Show all photos**.

## Interaction
- **Keyboard first.** Every action has a key, shown in tooltips, context menus, footers and the F1 panel. Arrow keys
  move through photos in reading order; Ctrl+− / Ctrl+= change density.
- **Right-click** a tile for the same actions as the keys, with the keys shown.
- **Drag and drop** a folder from File Explorer onto the library to import it; a full-window hint confirms the target.
- **Selection and focus are distinct**, visually and in behaviour.
- **Progress and cancellation** for every long operation, in the footer or the panel that started it.
- **Motion:** only the system's popup fade on menus, drop-downs and tooltips. No animated hover, page or panel
  transitions; they would slow culling.

## Accessibility
- Visible keyboard focus everywhere (accent ring or outline).
- Meaning never by colour alone: decisions have glyphs, labels have letters, warnings have ⚠, active filters also change
  their caption.
- Text contrast ≥ 4.5:1 for body, secondary and tertiary text on every surface they appear on.
- Icon-only controls have tooltips and automation names; tiles announce file name and status.
- Layouts reflow at narrow widths (the header actions drop to a second row; filter groups wrap) and respect Windows
  scaling.
