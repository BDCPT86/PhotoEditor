# AI and Image Analysis Strategy

## Selected models (Milestone 05)

| Capability | Model | Licence | Status |
|---|---|---|---|
| Face detection (+ face sharpness) | YuNet 2026may (OpenCV Zoo) | MIT | Optional, off by default; speed benchmarked; **quality not yet evaluated** |
| Scene similarity / suggested pick | DINOv2-small (Meta, ONNX export) | Apache-2.0 | Optional, off by default; speed benchmarked; **quality not yet evaluated** |

Source, pinned revision, size, SHA-256 and the alternatives that were rejected are in
[ADR 0005](decisions/0005-local-ai-models.md). Both models and the runtime are free to use, with no fees, and notices
are in `THIRD-PARTY-NOTICES.md`.

### Running an evaluation
1. Put the photos in a folder. You must have permission to use them; they stay on this machine.
2. Write `labels.csv` with the header `file,group,decision,faces`:
   - `group`: a name shared by shots of the same scene, blank if none
   - `decision`: `keep`, `reject` or blank
   - `faces`: the number of visible faces, or blank if not labelled
3. Run:
   `dotnet run --project tools/FrameForge.Evaluation -c Release -- evaluate --models <dir with the .onnx files> --photos <folder> --labels labels.csv --out report.md`
4. Report metrics only together with the set's size and content.

Speed only, with no photos needed:
`dotnet run --project tools/FrameForge.Evaluation -c Release -- benchmark --models <dir> --count 100`

To check faces visually, write `faces.csv` plus copies with boxes drawn on them (originals are untouched; the output
folder must be outside the photos folder):
`dotnet run --project tools/FrameForge.Evaluation -c Release -- annotate --models <dir> --photos <folder> --annotations <out folder>`

## Principle
Prefer proven local models and deterministic image metrics. Evaluate model accuracy, speed, memory, hardware compatibility, and licensing before shipping. AI is advisory: users retain control.

## Candidate capabilities

| Capability | Candidate approach | Notes |
|---|---|---|
| Blur/sharpness | OpenCV metrics (Laplacian variance, edge measures) | Heuristic; content-dependent |
| Exact duplicates | File hash | Reliable for byte-identical files |
| Near duplicates | Perceptual hash / embeddings | Tune threshold on real samples |
| Face detection | SCRFD or RetinaFace family | Verify ONNX export and licence |
| Face similarity | ArcFace family | Biometric-like data; handle locally and carefully |
| Semantic image search | CLIP embeddings | Verify model licence and embedding quality |
| Object detection | YOLO-family ONNX | Verify model and weights licence |
| Image aesthetic/quality | NIMA or evaluated alternative | Scores are subjective and domain-dependent |
| Eye closure | Landmark-based or dedicated classifier | Validate on varied faces and lighting |
| Sports action | Fine-tuned/custom classifier or embeddings + rules | Likely needs a labelled dataset |

## Implementation guidance
- Define interfaces before binding a model to UI.
- Model assets should be versioned and stored separately from app binaries where appropriate.
- Do not download models silently. Explain size, source, licence, and user consent.
- Support CPU fallback. GPU acceleration is optional and hardware-dependent.
- Cache outputs using model version + image identity + analysis settings.
- Make analysis cancellable and bound concurrency.
- Record model name/version in analysis results.
- Keep model outputs and user decisions separate.
- Never describe heuristic metrics as definitive AI judgements.

## Dataset and evaluation
Create a small evaluation set with permission to use the photos. Include:
- Sharp and blurred images
- Similar burst frames
- Low-light and high-contrast scenes
- Different camera bodies/lenses
- Sports action, portraits, group photos, landscapes
- Difficult cases and intentionally misleading examples

Track:
- Precision/recall for duplicate grouping
- Agreement with human picks
- False rejection rate (especially important)
- Runtime per 1,000 photos
- Peak RAM/VRAM
- CPU/GPU and machine details

## Licensing checklist
Before distribution, verify:
- Model architecture licence
- Pretrained weights licence
- Training dataset terms where relevant
- Runtime/dependency licence
- Redistribution and commercial-use permissions
- Attribution requirements
- Any restrictions on face recognition or biometric processing

Document each selected model in `docs/decisions/` with source, version, licence, checksum, and evaluation.
