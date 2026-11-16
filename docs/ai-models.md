# AI and Image Analysis Strategy

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
