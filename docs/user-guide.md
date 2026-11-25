# FrameForge user guide

FrameForge takes you from a card full of photos to a finished, edited selection, on your own computer: import a
folder, cull it with the keyboard, use analysis as hints, edit, and export.

**Three promises hold everywhere in the app:**
- **Your originals never change.** Photos are only ever read. Edits, decisions and analysis are stored separately, and
  export writes new files.
- **Nothing leaves your computer.** There is no cloud. Optional AI models run locally and are downloaded only when you
  say so.
- **Automatic tools only suggest.** Analysis badges, AI picks, Auto options and masks are hints or previews. Nothing
  changes until you apply it, and you can undo it.

This guide describes FrameForge as of Milestone 08 phase B (masks), with the redesigned interface. Press **F1** in the
app for the keyboard list at any time.

---

## Finding your way around
- **Sidebar (left):** under **Library**, the views: **All photos**, **Undecided** (no Keep, Review or Reject yet),
  **Kept**, **To review**, **Rejected**, **Last import** (the photos the most recent import added) and, only when there
  are any, **Problems** (missing, unreadable or offline files). Each shows how many photos it holds. Under **Folders**, every
  imported folder, nested under its parent; a folder includes its subfolders. A view and a folder combine: **Kept** with
  a folder chosen shows that folder's keepers. **Settings** and **About** are at the bottom.
- **Header:** the view you are in and how many photos it shows, then the main actions: **Import**, **Analyze**,
  **Compare**, **Presets** and **Export**, and **?** for the shortcut list.
- **Second row:** narrow the view by **Rating**, **Label** and **Analysis** finding; **Clear refinements** appears while
  one is on. On the right, **Sort** and the thumbnail size.
- **Footer:** progress for import and Analyze (with **Cancel**), the result of the last action, and the library's
  totals (keep, review, reject, undecided).

## 1. Import a folder
1. Choose **Import** (Ctrl+I) and pick a folder of JPEGs, or drop a folder from File Explorer onto the window. The
   small arrow beside Import has **Include subfolders**; the choice is remembered.
2. Import reads only each file's details (size, date, camera, exposure). Thumbnails are made as tiles scroll into
   view, so a large folder is usable at once.
3. You can cancel at any time. Importing the same folder again continues where it stopped, picks up new files, and
   never creates duplicates.

Files that can't be read are listed as **Unreadable**. Files deleted since the last import show as **Missing** the next
time that folder is imported. If an import finds no photos at all in a folder that has some in the library (an empty
card reader or network share, say), nothing is marked Missing, and the result says so.

### Photos on a drive that isn't connected
If the drive or network share a folder is on isn't there, or the folder has gone, its photos show **⚠ Offline** in the
corner of each thumbnail:
- The folder gets a ⚠ mark in the sidebar; hover over it for the reason ("Drive E: isn't connected.").
- A note above the grid says how many photos are offline. **Check again** (or F5) looks again after you plug the drive
  back in.
- Your decisions, ratings and edits are kept, and thumbnails already made still show, so you can go on culling. Opening
  a photo at full size needs the drive.

### A folder you moved: Locate folder…
If you moved or copied a folder to another place (a new drive, an archive folder), don't import it again as new. Instead:
1. Right-click the folder in the sidebar and choose **Locate folder…**, or choose it on the note above the grid.
2. Pick where the folder is now.
3. FrameForge looks for each photo at the same place inside the new folder, with the same size, and shows what it found:
   found, found with a different date (copied; these move too), and any it couldn't match.
4. Choose **Move … photos**. Only the library changes; no file is moved or renamed. Decisions, ratings, edits and
   analysis stay with each photo. Thumbnails are made again as you scroll.

Photos it couldn't match are left as they were. If you already imported the folder at its new place, those photos are
listed as "already in the library" and left alone.

## 2. Look at your photos
- **Grid:** arrow keys move, Shift/Ctrl+click or Shift+arrows select several. Selected photos get a light frame; the
  photo with keyboard focus also has a thin blue outline. Each tile shows your decision (✓ ? ✕, top right), stars and
  label under the photo, ✎ before the name of an edited photo, and the analysis findings that matter for culling (top
  left). Group size, face count and clipping appear when you point at or select a tile.
- **Thumbnail size:** three sizes, from the buttons at the right of the second row or **Ctrl+−** / **Ctrl+=**. The
  smallest hides file names. The size is remembered.
- **Right-click** a photo (or press the **Menu** key) for the same actions as the keys: open, Develop, compare,
  decisions, rating, label, similar group, analysis details and presets, each with its key shown.
- **Viewer:** press **Enter** on a photo. ← / → go to the previous or next photo, **Esc** goes back to the grid.
  - **Z** or a double-click switches between fit and 100 %. **+ / −** or the mouse wheel zoom, and dragging pans.
  - **F / Shift+F** jump the 100 % view from face to face, to check focus quickly (needs face analysis, see
    section 7).

## 3. Cull
| Key | Decision |
|---|---|
| **P** | Keep |
| **X** | Reject |
| **V** | Review later |
| **U** | Unset (no decision) |
| **1–5** / **0** | Stars / clear stars |
| **6 7 8 9** / **Shift+0** | Red, yellow, green, blue label / clear label |
| **Ctrl+Z / Ctrl+Y** | Undo / redo |

- In the **grid**, a key applies to every selected photo.
- In the **viewer** it applies to the photo shown, and after Keep, Reject, Review or Unset the next photo opens by
  itself. You can turn that off in **Settings → Culling**.
- Decisions are saved as you make them and survive a restart. Undo history does not.

**Work through what's left:** choose **Undecided** in the sidebar. Each photo you decide leaves the view, and the
selection moves on to the next one. The sidebar counts update as you go.

## 4. Views, folders, filters and sort
- **Views and folders** are in the sidebar (see *Finding your way around*). When two folders have the same name, enough
  of their path is shown to tell them apart, and the full path appears when you point at one.
- **Rating** (Any, or 1★ to 5★ meaning at least that many), **Label** (any, or one colour) and **Analysis** (likely
  blurry, clipping, similar groups, exact duplicates, AI pick, AI: faces soft, changed by Auto) narrow the current view
  further. A filter that is on turns its caption blue. **Clear refinements** sets them back to Any and keeps the view
  and folder.
- **Sort** by Folder, Capture time, File name or Rating. **F5** applies the filter and sort again, and checks for drives that were plugged back in.
- The number beside the heading counts the photos shown; "84 of 312" means the refinements hide the rest of the view.
  When nothing matches, **Show all photos** goes back to everything.

## 5. Compare
Select 2–4 photos and press **C** (or choose **Compare**). They appear side by side. **← / →** choose which one your
decision keys apply to (it has the light frame and **Active**), and **Esc** goes back. Beside each photo's name are its
**AI pick**, **◆ sharpest**, **≈ blurry?** and **☺ faces soft** findings: the ones that decide between similar shots.

## 6. Analyze (measurements, not AI)
**Analyze** measures every photo once and remembers the results; later runs only look at new or changed photos.
Nothing is decided or deleted for you.

| Mark on the tile | Meaning | Shown |
|---|---|---|
| **≈ blur** | The sharpness score is below your threshold. | Always |
| **◆** | The sharpest photo in its similar group. | Always |
| **= dup** | An identical file exists elsewhere in the library. | Always |
| **◐** | Too many pixels are pure white or pure black. | When you point at or select the tile |
| **▣ n** | One of n similar photos (a burst or the same scene). | When you point at or select the tile |

Point at any mark for its name.

- Press **I** to see the exact numbers and why each badge was given.
- Press **G** to select a photo's similar group, then **C** to compare it.
- The thresholds are in **Settings → Analysis** and apply instantly. The defaults are only starting points: tune them
  on your own photos. Changing the analysis resolution makes the next Analyze run measure again.

## 7. Optional AI models
FrameForge works fully without them. Each model is free to use, runs on this computer, and is downloaded only after
you confirm.

1. Open **Settings → AI models**. Each model lists what it is for, its size, source, licence and checksum.
2. Choose **Download…** and confirm with **I agree, download**. If you already have the file, use **Import file…**;
   either way the file is checked against its published checksum before it is used.
3. Under **AI analysis**, turn on *faces* and/or *scenes*, choose **Save**, and run **Analyze**.

| Model | What it adds |
|---|---|
| YuNet face detector | **☺ n** (faces found, shown when you point at a tile), **☺ soft** (all faces look soft), zoom to faces (F), and Auto crop to faces. |
| DINOv2 small | Groups similar scenes and suggests an **AI pick** per group, with Low, Medium or High confidence. **I** shows why. |
| YOLOX-s person detector + MobileSAM encoder + MobileSAM decoder | **Select subject** and **Background** masks in Develop (all three are needed). |

**Analysis → AI pick** and **Analysis → AI: faces soft** filter by those findings. AI results never change your decisions, and their
quality is reported only where it has been measured on a set you reviewed yourself. Treat them as hints.

## 8. Edit in Develop
Open a photo in the viewer and press **D**, or press **D** in the grid. The panel on the right edits the photo
non-destructively; the original never changes.

- **Light:** Exposure, Contrast, Highlights, Shadows. **Colour:** Temperature, Tint, Vibrance, Saturation.
- The preview updates as you drag. Double-click a slider to reset it.
- Each pause while editing is one undo step: **Ctrl+Z / Ctrl+Y** (or ↶ ↷). Hold **\\** or use ◐ to see the original;
  ⟲ resets everything (also undoable).
- Edits save themselves. Edited photos show **✎** in the grid, and their thumbnails show the edit.

**Crop and straighten (R):** drag the frame or its corners, pick an aspect ratio (Free, Original, 1:1, 4:5, 3:2,
16:9), rotate with ⟲ ⟳, and use **Straighten**. Arrow keys move the frame, Ctrl+arrows resize it.

**Auto:** **Light**, **Level** and **Crop** each show up to three options below the photo, each with its reason. ← / →
or 1–3 preview one on the photo, **Enter** applies it as one undo step, and **Esc** dismisses them. **Level** draws the
line it found. **Crop** frames the main faces at the ratio chosen under Crop and needs face analysis. Auto never
applies anything on its own.

JPEGs are 8-bit: highlights that are already blown can't be recovered.

## 9. Masks: edit part of the photo
The **Masks** section in Develop adjusts one area at a time: brighten the player, cool the sky, calm a busy background.

**Add a mask:**
- **Subject** selects the main people (the largest ones). It needs the three subject models (section 7).
- **Background** is everything except the subject.
- **Sky** selects blue or overcast sky from the top edge. It needs no model.

Up to 8 masks per photo. A new mask is selected and shown on the photo straight away.

**Each mask** has its own Exposure, Contrast, Highlights, Shadows, Temperature, Tint, Vibrance and Saturation, with the
same ranges as the global ones. They apply on top of the global settings. In the list:
- **◑ Invert** adjusts everything outside the mask instead.
- **✕ Delete** removes it (undoable).
- The text under the name says what was found ("1 person", "Sky covers 31 % of the photo"). While a mask is being
  worked out, a progress bar and **Cancel** show; the rest of the edit keeps working.
- If a mask can't be made, ⚠ and the reason show instead ("No people found. Click the subject to select it.") with
  **Retry**. Your other settings still apply.

**Show (O):** darkens and greys everything outside the selected mask and outlines its edge in amber. It works zoomed
in too.

**Refine (M), fix a mask with clicks:**
- **Click** to add: click a person to add them, or click anything else (a hidden player, the ball) to add that area.
- **Alt+click** or **right-click** to remove: on a person it removes them ("not this umpire"); elsewhere it trims the
  outline.
- Clicks show as **⊕** and **⊖** on the photo. **Clear clicks** removes them all, **Esc** stops refining.
- You can zoom and pan while refining: a drag pans, only a click without a drag places a point.
- After the first time, each click takes well under a second, because only the last part of the model runs again.

**Good to know:**
- The first Subject mask on a photo takes about half a second on a quiet laptop, and a few seconds when the computer
  is busy. It is remembered, so opening the photo again is quick.
- Sky works best on blue or evenly grey sky. It leaves out textured or darker areas at the top, such as walls and
  panels. A very small or hazy patch of sky can be missed: click it to add it. If a smooth, bright wall is picked up,
  Alt+click it.
- Grid thumbnails show masks once they have been worked out in Develop.

## 10. Presets: one look for many photos
- **Save:** in Develop, under **Presets**, type a name, tick what to include (Exposure, White balance, Tone, Colour,
  Crop and rotation, Masks), and choose **Save preset**.
- **Apply to one photo:** select it in the Presets list in Develop and choose **Apply** (one undo step).
- **Apply to many:** select photos in the grid, choose **Presets** (or right-click → **Apply preset or Auto…**), pick one
  and **Apply to selected**. There's
  progress and cancel, and **Undo last apply** puts every photo back.
- A preset changes only the settings it was saved with.
- **Presets with masks** work out each photo's own masks as they go (clicks are never copied between photos), so they
  take longer: the panel says roughly how long before you start (a subject takes 0.6–3 s per photo) and counts down while
  it runs. If a mask can't be made for a photo (no people, no sky), that photo gets the rest of the preset without the
  mask and is listed with the reason. A preset with a subject mask needs the subject models (section 7); without them it
  says so and doesn't start.
- **Auto for many:** in the same panel, the **Auto** section runs Light, Level and/or Crop on every selected photo in
  that order, with the option you choose now. Photos it couldn't help are listed, and **Analysis → Changed by Auto** shows what it
  changed.

## 11. Export
Choose **Export** in the header, pick which **Photos** (all marked Keep, or those shown in the grid), and what to
**Export as**, then **Choose destination and export…**. To export only one folder's keepers, choose **Kept** and that
folder in the sidebar, then **Photos shown in the grid**.
- **Copy files to a folder:** the originals, copied unchanged.
- **Edited JPEGs:** new JPEGs with each photo's edits, masks included, at the **Quality** and **Longest side** you
  choose, optionally keeping the camera metadata (EXIF). Masks not worked out yet are worked out during the export. If
  one can't be made, the JPEG is still saved with the rest of the edit, and the result says which photo and why.
- **Selection manifest (CSV):** a list of the photos and your decisions, with no image files.

Existing files are never replaced: a name clash is saved as `name (2).jpg`. If you export into a folder that is part of
your library, FrameForge warns you that the copies will appear as new photos at the next import.

## 12. Keyboard
| Where | Keys | Action |
|---|---|---|
| Anywhere | **F1** or **?** | Show or hide the shortcut list |
| Anywhere | **Ctrl+I** | Import folder |
| Anywhere | **Ctrl+1 / 2 / 3** | Library, Settings, About |
| Grid | **Ctrl+− / Ctrl+=** | Smaller / larger thumbnails |
| Grid | Right-click, **Menu** key | Actions for the selected photos |
| Grid / viewer | **P X V U** | Keep, reject, review, unset |
| Grid / viewer | **1–5**, **0** | Stars, clear stars |
| Grid / viewer | **6–9**, **Shift+0** | Labels, clear label |
| Grid / viewer | **Ctrl+Z / Ctrl+Y** | Undo / redo |
| Grid | **Enter** | Open the viewer |
| Grid | **C** | Compare 2–4 selected photos |
| Grid | **G** / **I** | Select the similar group / show analysis details |
| Grid | **F5** | Re-apply filter and sort; look for offline drives again |
| Viewer | **← / →**, **Esc** | Previous / next photo, back to the grid |
| Viewer | **Z**, **+ / −** | Fit ↔ 100 %, zoom |
| Viewer | **F / Shift+F** | Next / previous face |
| Viewer | **D** | Open or close Develop |
| Develop | **R** | Crop and straighten |
| Develop | **\\** (hold) | See the original |
| Develop | **O** | Show or hide the selected mask |
| Develop | **M**, **Esc** | Refine the selected mask, stop refining |
| Develop, Auto options | **← / →**, **1–3**, **Enter**, **Esc** | Preview, apply, dismiss |

## 13. Where your data is
| What | Where |
|---|---|
| Library database (decisions, edits, analysis) | `%LOCALAPPDATA%\FrameForge\frameforge.db` |
| Logs | `%LOCALAPPDATA%\FrameForge\logs\` |
| Thumbnails (safe to delete) | `%LOCALAPPDATA%\FrameForge\thumbnails\` |
| Masks (safe to delete) | `%LOCALAPPDATA%\FrameForge\masks\` |
| AI models | `%LOCALAPPDATA%\FrameForge\models\` |

The **About** page shows the data folder, the logs folder and the database. Deleting the thumbnail or mask folder only makes FrameForge rebuild them.
Deleting the whole `FrameForge` folder resets the app; your photos are not touched.

## 14. Limits worth knowing
- JPEG only for now (RAW is planned).
- At the largest thumbnail size on a high-DPI screen (150 % and up), thumbnails can look slightly soft: they are made at
  320 px.
- There are no collections yet; the sidebar shows views and folders only.
- AI runs on the CPU. A subject mask takes from half a second to a few seconds the first time, depending on how busy
  the computer is.
- The quality of analysis, AI picks and masks has only been measured on small sets the developer reviewed. Use them
  as hints, and check before you rely on them.
