# Judging the Auto tools (Milestone 08)

Two short sessions that measure Auto light and Auto crop against your own taste, as the M08 brief asks: a blind A/B
for Auto light, and your own crops compared with Auto crop. Each one is a web page you open from disk. Nothing is
uploaded, and your photos are only read.

## 1. Build the sheets

From the repository root (pick any **new** folders outside your photos folder):

```bash
dotnet run --project tools/FrameForge.Evaluation -- light-sheet --photos "D:\Photos\Match" --sheet "D:\Judging\light" --count 30
```

```bash
dotnet run --project tools/FrameForge.Evaluation -- crop-sheet --models "D:\Models" --photos "D:\Photos\Match" --sheet "D:\Judging\crop" --count 30
```

- `--count` is the number of pairs or photos (default 30). About 30 takes 5–10 minutes, and fewer gives wide error
  bars.
- `--seed` picks a different random sample (default 1). The same seed rebuilds the same sheet.
- **Light sheet:** it skips photos Auto would leave unchanged.
- **Crop sheet:** it needs the face model files (the same ones as in the app). It keeps only photos with a main face,
  because Auto crop has nothing to frame otherwise. Photos Auto calls "already framed loosely" stay in, because
  declining to crop is a decision too.
- A sheet folder holds `index.html`, `img/` (downscaled copies) and `sheet.json`. **Don't open `sheet.json` before
  judging:** it says which side is Auto.

## 2. Judge

Open `index.html` in Edge or Chrome.

- **Auto light:** pick the better-looking photo with **←** (left), **→** (right) or **↓** (no real difference).
  - One side is as shot and one is Auto light's "Natural" option, in random order.
  - Judge the photo as a finished picture.
- **Crop:** crop each photo the way you would for sharing.
  - Drag to draw a crop, drag inside it to move it, and drag a corner to resize.
  - Pick a ratio, or keep **Free**.
  - Press **N** to keep the whole frame, and **Enter** for the next photo.
  - Faces aren't marked, on purpose.
- **Backspace** goes back. Answers are remembered in the browser, so you can stop and come back.
- When you're done, press **Save answers**. The browser saves `light-answers-….csv` or `crop-answers-….csv`.

## 3. Score

```bash
dotnet run --project tools/FrameForge.Evaluation -- score-sheet --sheet "D:\Judging\light" --answers "C:\Users\me\Downloads\light-answers-light-1-….csv" --out light-report.md
```

**The light report gives:**
- how often you preferred Auto, as shot, or neither
- the share of decided pairs that went to Auto, with a 95 % interval and an exact sign test
- a table of what Auto changed on each photo and which one you picked

**The crop report gives:**
- **Overlap:** the overlap (IoU, where 1 means identical) between your crop and Auto's closest option. Auto is computed
  at *your* crop's aspect ratio, so this measures framing, not the choice of ratio.
- **Medium alone:** the same overlap for the Medium option, which batch Auto uses by default.
- **Whole frame kept:** how often Auto also left the frame alone where you kept it, and how often Auto declined to crop
  a photo you cropped.
- **Looser or tighter:** your crop's area divided by Auto's closest option, and the median shift of the centre. These
  show which way to adjust.
- **Tighter than Auto allows:** how many of your crops are tighter than Auto's minimum size (2,000 px long side).
  Auto can't match those by design.

## Reading the results

These numbers describe agreement with one person on one set of photos. They don't measure general quality, and
they're too small to tune thresholds finely. Read the interval before the percentage. For example, 7 of 10 is
"somewhere between 40 % and 89 %".

Keep sheets and reports outside the repository: they contain copies of your photos.

## Results

### Round 1 (2026-11-17, 30 + 30 sports photos, one judge)
**Auto light**, first version:
- As shot 14, Auto 2, no real difference 14. Auto won 12 % of decided pairs (95 % interval 3–36 %), sign test
  p = 0.004.
- The 6 pairs darkened by 0.30 EV all went to "as shot".
- Mild warming (+2 to +24) never won.
- Small highlight pull-downs on barely clipped photos often lost too.
- Both Auto wins had a shadow lift.

**Auto crop**, first version:
- The judge cropped 17 photos and kept the whole frame on 13.
- Median IoU with Auto's closest option was 0.37; Medium alone was 0.19.
- Every crop was looser than Auto's closest option (median 2.7× the area): about 21 face heights tall, with the eyes
  at 28 % from the top.

**Retune** (see [ADR 0007](decisions/0007-auto-editing-tools.md)):
- Auto light no longer darkens. It changes temperature only for a strong cast and highlights only for real clipping.
- Auto crop options are 12, 20 and 30 face heights tall, with the eyes at 28 %. A crop that keeps nearly the whole
  frame isn't offered.

Re-scoring the same crop answers with the retuned crop gave a median IoU of 0.86 (Medium alone 0.80, 11 of 17 close
matches), and Auto left the frame alone on 8 of the 13 where the judge did. This is **in-sample**: the numbers were
tuned on these answers, so it only shows the retune fits them. Light can't be re-scored without new judging.

### Round 2 (2026-11-17, retuned, fresh photos)
New sheets with fresh random samples (seeds 7 and 8). Of the 60 photos, 5 were also in round 1, and none appears twice
in the same test. While building the light sheet, Auto left 24 of the 54 photos it sampled unchanged.

**Auto light**, retuned:
- As shot 11, Auto 5, no real difference 14. Auto won 31 % of decided pairs (95 % interval 14–56 %), sign test
  p = 0.21. That is no longer a clear loss, but not a win either.
- **Shadow lift**: 7 pairs, Auto 3, as shot 1, no difference 3. It is the only change that keeps winning.
- **Highlights only**: 22 pairs, Auto 2, as shot 9, no difference 11. It lost in both rounds. Even −40 on heavily
  clipped photos split 2–2.
- **Temperature** (−25, one pair): went to "as shot".
- Some "as shot" picks were against changes as small as highlights −4, which are hard to see. Part of the result is
  probably noise.

**Auto crop**, retuned:
- The judge cropped 8 photos and kept the whole frame on 22. The sample now includes photos Auto calls already framed;
  3 such photos were drawn, and the judge kept the frame on all 3.
- On the 8 crops: median IoU 0.77 (Medium alone 0.67), 4 close matches, none different (< 0.5). Median area ratio
  0.88, so the judge was slightly tighter than Auto, against 2.7× looser in round 1. The size is now about right.
- **Whether to crop at all**: where the judge kept the whole frame, Auto still offered a crop on 19 of 22. Many of
  those crops were mild (IoU with the whole frame 0.8–0.9). This is now the main disagreement. Auto crop proposes a
  framing; it does not decide whether a photo needs cropping.
