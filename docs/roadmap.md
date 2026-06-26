# DedupSharp roadmap

High-level direction, from the README. Update as items land.

## Near term
- Exact binary core (initial version) — active
- Basic tests and benchmark scaffolding
- Optimised binary comparison (SIMD / AVX2, tuned buffer sizes)
- Faster non-crypto hashing (e.g. XXH / BLAKE3) for large groups
- CLI frontend (`DedupSharp.Cli`)

## Later
- Media core for images — perceptual hashes, similarity detection (resized,
  recompressed, small edits)
- Audio core — PCM-exact (container/metadata agnostic) plus spectrogram-based
  perceptual matching that reuses the image core's hashing engine
- Windows GUI (WinForms/WPF), possibly plugins (e.g. Total Commander)
