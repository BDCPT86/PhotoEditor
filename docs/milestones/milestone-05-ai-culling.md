# Milestone 05 — AI-Assisted Culling

## Goal
Introduce local AI models as optional recommendations, not automatic final decisions.

## Deliverables
- Model/runtime abstraction
- Model metadata and licence registry
- Explicit model acquisition/consent flow
- CPU inference baseline; optional accelerator only after validation
- Image embeddings and/or detection selected after evaluation
- AI recommendations with score, confidence/context, and explanation
- Analysis cache keyed by model version and settings
- Settings to enable/disable model capabilities
- Benchmark/evaluation harness and report

## Acceptance criteria
- Application works when no AI model is installed
- Model errors do not break browsing/culling
- User decisions are never overwritten by AI
- Inference is cancellable and concurrency-limited
- Model licence/source/version is documented
- Report latency and memory on target hardware
- Human-reviewed evaluation set exists before claiming quality

## Prompt
Implement only Milestone 05 after proposing and obtaining approval for the model choices. Verify current ONNX Runtime compatibility and each model/weights licence. Keep model code behind interfaces. Never silently download weights. AI output must be advisory and separate from human decisions. Build, test, benchmark, and document accuracy limitations.
