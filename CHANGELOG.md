# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.8.0] - 2026-05-20

### Fixed

- `DngMetadata` now prefers the raw image SubIFD for shape-of-image tags (`Image Width`, `Image Length`, `Bits Per Sample`, `Compression`, `Photometric Interpretation`, `CFA Pattern`, `Black Level`, `White Level`) instead of reporting the small thumbnail in IFD0. Camera tags (`Make`, `Model`, EXIF) still come from IFD0 / the EXIF SubIFD as before. Verified against the MIT-Adobe FiveK DNGs.

### Added

- TIFF LZW decoder (`Core/LzwDecoder.cs`, compression 5). Implements the TIFF "early code-width bump" semantics and the LZW special-case for `k == nextCode`.
- TIFF Adobe Deflate / zlib decoder (`Core/DeflateDecoder.cs`, compression 8 and the legacy 32946) via `System.IO.Compression.ZLibStream`.
- `DngRawReader` now opens uncompressed, Lossless JPEG, LZW and Deflate TIFF / DNG payloads — covering every compression bundled with the sample assets.
- `WarpFisheye` preview: per-plane 4-coefficient polynomial in `atan(r)` with bicubic backward sampling. The opcode is now fully read, written and previewed.
- Headless `dng-opcodes` CLI (new `Cli/` project): `list`, `extract`, `inject`, `metadata` and `preview` commands. Works without the WPF GUI; useful for batch / scripted opcode editing.
- `Core/TiffWriter.cs`: minimal 16-bit RGB TIFF encoder used by the CLI `preview` command (and a building block for any future "save as 16-bit TIFF" in the GUI).
- `OpcodesImplementation.Apply` and `OpcodesImplementation.ApplyGamma` — shared dispatch helpers so the WPF VM and the CLI go through the same code path for every opcode.
- `DngRawReader` also accepts plain RGB TIFFs (photometric 2), with a smarter best-IFD picker that prefers CFA/LinearRaw over RGB thumbnails when both are present.
- CLI integration tests: end-to-end `preview` and `list` exercised by building inputs in-memory with `TiffWriter` / `TiffFile.WriteOpcodeList`.
- Lossless JPEG (SOF3) decoder so DNGs with Compression = 7 — the format most cameras emit — open directly. All seven predictors, byte-stuffing and Huffman tables are handled.
- Tiled raw DNG layout support (TileOffsets / TileByteCounts / TileWidth / TileLength), with arbitrary tile sizes including partial edge tiles.
- LinearRaw DNG support (photometric 34892, three interleaved RGB samples per pixel — no demosaic step).
- Metadata viewer panel in the WPF window showing the common EXIF / DNG tags of the open file (Make, Model, ISO, exposure, colour matrices, calibration illuminants, etc.). Lives in a new tab next to the parameter grid.
- `TiffFile.FormatEntryValue` public helper that formats any TIFF entry as a human-readable string, including RATIONAL and FLOAT / DOUBLE arrays.
- `DngMetadata.Read` helper that walks IFD0, SubIFDs and the EXIF SubIFD, returning a friendly Name / Value list with light per-tag prettification (compression code → name, calibration illuminant → name, units appended to focal length / exposure / aperture).
- Tests: new LJPEG decoder tests (all-zero differences, mixed-difference Huffman roundtrip), tiled-uncompressed DNG, compressed (LJPEG-tile) DNG end-to-end, LinearRaw DNG, metadata reader.

### Changed

- `DngRawReader` refactored into layered sample-loader / sample-decoder / output-formatter stages so each file layout (strip / tile) and compression (uncompressed / LJPEG) can be combined freely.
- Documentation: README updated to reflect the broader DNG coverage; FAQ no longer warns about compressed DNGs.

## [0.7.0] - 2026-05-20

### Added

- New opcode read / write / preview implementations: `MapTable`, `MapPolynomial`, `DeltaPerRow`, `DeltaPerColumn`, `ScalePerRow`, `ScalePerColumn`, `FixBadPixelsConstant`, `FixBadPixelsList`.
- `WarpFisheye` read and write (preview not yet implemented).
- `WarpRectilinear` now supports multi-plane warps (chromatic-aberration correction) and bicubic (Catmull-Rom) resampling.
- Built-in TIFF/DNG IFD parser (`Core/TiffFile.cs`) that reads and writes the `OpcodeList1/2/3` tags directly, including SubIFDs, both endiannesses and inline-vs-offset value fields.
- Built-in DNG raw open via bilinear demosaic (`Core/DngRawReader.cs`) for uncompressed strip-based DNGs — no more `dcraw_emu` step.
- "Add Opcode" picker re-enabled in the UI so the new opcodes are reachable.
- Async `ApplyOpcodes` runs the pixel work off the UI thread, with a re-entrancy guard that coalesces rapid edits (for example slider drags) into one trailing pass.
- `[RelayCommand]`-based commands replace the code-behind button handlers.
- Friendly error dialogs for missing files, malformed input and opcode apply failures.
- Import and Export now cover OpcodeList1, OpcodeList2 and OpcodeList3 (the WPF version previously only exported `OpcodeList3`).
- New `Core` class library extracted from the WPF project (`Core/DngOpcodesEditor.Core.csproj`, `net9.0`, no WPF dependency).
- New `Tests` project (`Tests/DngOpcodesEditor.Tests.csproj`) with xUnit reader/writer round-trip, TIFF parser and demosaic tests — 19 tests in total.

### Changed

- Target framework is now `net9.0-windows7.0` (was `net6.0-windows`).
- `OpcodesWriter` preserves each opcode's actual DNG version and flags on round-trip rather than hardcoding `DNG_VERSION_1_3_0_0` / `OptionalPreview`.
- `OpcodesReader` resyncs to each opcode using the declared `bytesCount`, so a partially-parsed opcode no longer desynchronises the stream.
- `BigEndianStreamExtension` uses `Stream.ReadExactly` to avoid silent short reads.
- README rewritten with a project-layout table, opcode coverage table and Getting started section.

### Removed

- External `exiftool.exe` dependency and the associated csproj copy entry.

### Fixed

- `GainMap` rectangle test used the wrong boolean precedence (`||` instead of `&&`), which caused the gain to be applied well outside the configured rectangle.
- `GainMap` gain-plane de-interleaving used `planes` instead of `mapPlanes` as the stride, producing wrong gains when the two differed.
- `GainMap` now replicates edge values when the sample falls outside the map (via clamping `xMap`/`yMap`) and exits early on an empty map.
- `Image.Open` resolves the filename to an absolute path before constructing the `Uri`, so file-dialog paths no longer throw a `UriFormatException`.
- `Opcode.Parameters` is now cached, so the WPF DataGrid re-reading it no longer stacks fresh `PropertyChanged` handlers and triggers `ApplyOpcodes` multiple times per edit.

## [0.6.0] - 2024

### Added

- Span-based pixel access and `.NET 9` target — opcode processing is roughly 40% faster.
- Drag-and-drop support for images, `.bin` payloads and DNG files.
- "Decode Input Gamma" and "Encode Output Gamma" toggles for working with both linear and gamma-encoded reference images.
- Mouse-position read-out with per-pixel RGB values for source and destination.
- Solid-colour TIFF samples for testing.
- Wait cursor while a slow operation is running.

### Changed

- Internal pixel format switched to 16-bit per channel.
- Automatic detection of linear (non-gamma) input images.

### Fixed

- Drag-and-drop edge cases.

## [0.5.0 and earlier]

- `GainMap` opcode implementation.
- Opcodes can be individually enabled / disabled.
- Support for OpcodeList index 2 and 3 on import.
- "Clear" button to wipe the opcode chain.
- Initial implementations of `WarpRectilinear` (single-plane, nearest-neighbour), `FixVignetteRadial` and `TrimBounds`.
- Initial WPF MVVM editor with reader/writer for the OpcodeList binary format.

[0.8.0]: https://github.com/electro-logic/DngOpcodesEditor/compare/v0.7...v0.8
[0.7.0]: https://github.com/electro-logic/DngOpcodesEditor/compare/v0.6...v0.7
[0.6.0]: https://github.com/electro-logic/DngOpcodesEditor/releases/tag/v0.6
