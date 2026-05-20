# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[0.7.0]: https://github.com/electro-logic/DngOpcodesEditor/compare/v0.6...v0.7
[0.6.0]: https://github.com/electro-logic/DngOpcodesEditor/releases/tag/v0.6
