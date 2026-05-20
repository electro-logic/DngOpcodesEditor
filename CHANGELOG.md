# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- **OpcodeList2 GainMaps are now applied to the linearised CFA buffer before demosaicing, not on the demosaiced RGB.** Pixel 6 DNGs (and any other camera that ships a per-Bayer-plane lens-shading correction in L2) used to render dramatically wrong colours — every preview came out bright red. The Pixel 6 ships four GainMaps in OpcodeList2 with `plane=0`, `(rowPitch, colPitch) = (2, 2)` and `(top, left)` offsets of `(0,0)/(0,1)/(1,0)/(1,1)`, which is the spec idiom for addressing the four Bayer subpixels in a 2×2 CFA cell; the gain values average ~2× and peak around 4.5× toward the corners. Applied to demosaiced RGB they all collapsed onto the R channel (the only channel at index `plane=0`), so every pixel got R≈2× while G and B stayed put — hence the red shift. The fix:
  - `DngRawReader` now linearises CFA samples first (new `LineariseCfaInPlace`), then runs L2 GainMaps over that buffer (new `ApplyGainMapToCfa`), then demosaics (new `BilinearDemosaicLinearised`).
  - `MainWindowVM.ApplyOpcodes` and `CLI preview` skip opcodes with `ListIndex == 2` to avoid double-application — they're already baked into the decoded image.
  - Known limitation: editing an L2 opcode's parameters or toggling it in the UI no longer affects the preview without reloading the file. The L2 list is still shown for inspection.
  - Other CFA-targeting L2 opcode types (`FixBadPixels*`, `MapTable`, `MapPolynomial`, `Delta*Per*`, `Scale*Per*`) are still skipped at decode (with a debug log) — no real-world samples in the corpus exercise them. Treating them analogously is future work.
- 3 new tests under `Tests/OpcodeList2CfaTests.cs` pin: (1) a GainMap with `pitch=2` only multiplies its target Bayer subgrid, (2) overflow clamps to 65 535 rather than wrapping, (3) bilinear interpolation across the gain field matches expected midpoint values. 74 tests pass.
- Verified visually on the corpus: Pixel 6 now renders the actual scene (teal clock on wood floor), Mavic 3 Pro sunset and Phantom 4 autumn landscape render identically to before (those files carry no L2 opcodes — only L1 or L3).

## [0.8.8] - 2026-05-20

### Fixed

- **Three quantisation points across the pipeline were truncating instead of rounding** — small per-pixel errors that accumulated into visible banding when many stages run in sequence. Identified during a precision audit:
  - **`DngToneCurve.Apply`** did `lut[v >> 4]`, which threw away the bottom 4 bits of the 16-bit input *and* — because the 4096-entry LUT samples on `i/4095` axis while the buffer is in 16-bit-pixel space — drifted the output upward by up to ~12 LSB mid-range. The output could only take 4096 distinct values per channel, producing visible posterisation on smooth tonal gradients. Replaced with a float-rescale into LUT-index space + linear interpolation between adjacent entries; identity round-trip is now within ±1 LSB across the full 16-bit range, and the output uses all 65536 levels.
  - **`PixelBuffer.Resize`** (FHD preview downsample) accumulated `long` channel sums and then did `(ushort)(sum / count)`, which truncates toward zero — every output pixel was biased dark by up to 1 LSB. Now uses round-half-up integer division.
  - **`OpcodesImplementation.NeighborAverage`** (used by `FixBadPixels{Constant,List}`) had the same truncating average — every fixed bad pixel was slightly dark. Now rounds.
- 5 new tests under `Tests/PipelinePrecisionTests.cs` pin each fix: tone curve monotonicity across all 65536 inputs, ≥60 000 distinct outputs from the identity curve, near-lossless identity round-trip, resize rounds half-up, and the bad-pixel inpaint uses the rounded mean. All 71 tests pass.

### Changed

- **One file per opcode under `Core/Opcodes/`.** `OpcodesImplementation` is now a `partial` class split across 14 files: the central `OpcodesImplementation.cs` keeps the `Apply` dispatcher, the gamma / sRGB helpers, and the shared private helpers (`ApplyArea`, `SampleBicubicChannel`, `CubicWeight`, `NeighborAverage`, `FixPixel`); each of the 13 supported opcodes lives in its own file alongside a doc-comment block that explains the DNG-spec meaning, the parameters, the typical OpcodeList placement, and any approximation notes. No behaviour change — 62 tests still pass.

### Added

- **`ProfileHueSatMap` (DNG tags 50937 / 50938 / 50939) now drives per-hue saturation and value tweaks.** Read on file load and applied between the colour matrix and the tone curve, per the DNG spec ordering. Trilinear interpolation in HSV with hue wrap-around. Closes the saturation gap visible against Windows Photos on DJI Phantom 4 / Mavic 3 Pro DNGs, especially in the high-V high-S cells the manufacturer targets (skies, vivid colours). Uses `HueSatMap2` (D65) by default — HueSatMap1 + illuminant-interpolated blending remain future work (`docs/opcode-support.md`).
- 5 new tests covering identity table, sat-boost on grey pixels (stays grey), sat-boost on coloured pixels (correctly increases sat), 120° hue rotation, and invalid-input handling.
- **Display Dither checkbox** in the WPF window — toggles `Image.DitherDisplay` live so you can A/B the dithered vs untouched 16→8 conversion without restarting. Bound to a `MainWindowVM.DitherDisplay` `[ObservableProperty]`; changes redraw the preview from the unchanged 16-bit buffer (no full pipeline rerun).

## [0.8.7] - 2026-05-20

### Added

- **TPDF dither at the WPF display 16→8 conversion.** The preview previously went through `FormatConvertedBitmap` (`Rgba64 → Bgr24`), which truncates and can produce visible banding in smooth gradients (skies, gradients). `Image.Update` now builds the on-screen Bgr32 bitmap manually with per-pixel triangular-PDF dither (±1 8-bit LSB, per-row seeded RNG for stable noise pattern across redraws), so the 8-bit quantisation noise stays incoherent and gradients look clean. The internal 16-bit buffer and the TIFF save path are unaffected. Toggle via `Image.DitherDisplay`.
- **EXIF Orientation tag (274) is now honoured when opening DNGs.** `PixelBuffer.ApplyOrientation` implements all eight EXIF orientations (identity / mirror / rotate 180 / rotate 90 CW / rotate 90 CCW / two transposes), and `DngRawReader.Read` applies the IFD0 orientation after demosaic. Files in the FiveK test set that were previously stuck in raw-sensor orientation now load correctly: `a0003-NKIM_MG_8178` (90 CW) and `a0074-WP_CRW_0343` (90 CCW) come out portrait, `a0009-kme_372` (180) right-side-up.
- `Orientation` added to the metadata viewer with friendly value names ("Normal", "Rotate 90 CW", etc.) instead of the numeric code.
- 5 new tests covering identity / 180 / 90 CW / 90 CCW / out-of-range orientations on a 2×3 test buffer.

### Fixed

- **Sky / blue channels no longer come out purple** on DJI Phantom 4 and similar DNGs. `ColorTransform.BuildCameraToSrgb` used a post-hoc row normalisation (`m /= white`) to force the scene white to map exactly to `(1, 1, 1)` sRGB. That made the white correct but bent the rest of the gamut — blues in particular ended up with too much red mixed in, so a daylight sky read as magenta/violet. Replaced with the proper DNG-spec WB diagonal: compute `referenceNeutral = ColorMatrix · D50_white`, build `D[i] = referenceNeutral[i] / AsShotNeutral[i]`, and form `camera→sRGB = XyzToSrgb_D65 · Bradford_D50→D65 · inv(ColorMatrix) · diag(D)`. With this formulation, the scene white still maps to ≈`(1, 1, 1)` (within ~0.04% — D65 sRGB rounding) while off-white colours pass through linearly. A 20161129-DJI_0014 sample sky pixel that previously read R≈22k, G≈26k, B≈22k (R ≈ B → magenta) now reads R=22937, G=26290, B=35627 — properly blue-dominant.
- `ColorTransformTests.BuiltCameraToSrgbMapsAsShotWhiteToWhite` loosened to `±0.01` instead of exact equality (the new formulation hits white via XyzToSrgb_D65·Bradford·D50_white rather than by construction).

## [0.8.6] - 2026-05-20

### Added

- **DNG `ProfileToneCurve` (tag 50940)** now drives the preview's tonal rendering. Read once on file load, baked into a 4096-entry 16-bit LUT (linear interpolation between control points), then applied per-channel after the colour matrix and before gamma encode. DJI Mavic 3 Pro DNGs in the test set ship a 256-point shoulder curve that the editor was previously ignoring.
- `Core/DngToneCurve.cs` with `FromControlPoints` / `BuildLut` / `Apply` plus tests (identity curve, midtone-halving curve, invalid-input handling).
- `DngColorInfo.ToneCurve` carries the LUT alongside the existing colour metadata.
- `DngColorInfo` now reads `BaselineExposure` (tag 50730) and `ColorTransform.BuildCameraToSrgb` folds it into the camera-to-sRGB matrix as a uniform `2^stops` gain. Drone DNGs that ship with positive baseline exposure (DJI Mavic 3 Pro frames in the test set carry up to +0.86 EV) now preview at the brightness the manufacturer recommends.
- New test asserting `BaselineExposure` scales every cell of the matrix by `2^stops`.

### Fixed

- **Magenta cast in clipped highlights**: `ColorTransform.Apply` now takes an optional `AsShotNeutral` and, when supplied, blends saturating pixels (any WB'd channel above 0.95) toward neutral white before running them through the colour matrix. Highlights that previously came out tinted magenta because one channel clipped before the others now blow out cleanly to white. The MainWindowVM and CLI pass the as-shot neutral through automatically when the colour transform is enabled.
- Two new tests cover the desaturation path (saturating-blue pixel becomes white; mid-grey pixel is untouched).

### Changed

- **Gamma encode / decode now use the proper sRGB transfer function (IEC 61966-2-1)** instead of the rough `pow(c, 1/2.2)` approximation. New `OpcodesImplementation.ApplySrgbEncode` (OETF) and `ApplySrgbDecode` (EOTF) methods — piecewise functions with a small linear segment in shadows and a 2.4-gamma power segment elsewhere. MainWindowVM and the CLI's `preview` command both route through them. Visible improvement is mainly in shadow detail; the rest of the curve matches what monitors and image viewers expect. The legacy `ApplyGamma(buffer, exponent)` helper stays in Core for explicit power-curve uses. UI checkbox labels updated to "Decode Input (sRGB)" / "Encode Output (sRGB)". CLI flag names (`--decode-input-gamma`, `--no-encode-gamma`) unchanged.
- **All file dialogs now start in the dedicated DNG sample folder `D:\DngOpcodesEditor\Samples\dng`** (when present, with the previous repo `Samples/` and bin-output fallbacks behind it) and remember the most recently used folder for the next dialog. Applies to Open / Import / Save / Export commands alike.

### Changed

- Open dialogs now start in the **project's source `Samples/` folder** (resolved from the current working directory, then by walking up from the binary location until the `.csproj` is found). Previously they pointed at the bin output's linked copy, so newly-added test images required a rebuild to show up.

### Notes

- The full DNG colour stack the editor applies for the live preview is now: **BlackLevel + WhiteLevel** linearisation (`DngRawReader`) → **bilinear demosaic** → **opcode chain** → **AsShotNeutral white balance + ColorMatrix to sRGB + Bradford D50→D65 + BaselineExposure** (`ColorTransform`) → **gamma encode**. Tone curve and per-illuminant ColorMatrix blending are still future work.

## [0.8.5] - 2026-05-20

### Added

- **One-click "Open DNG (image + opcodes)" command and button** that loads a DNG, clears the existing opcode chain, and imports the file's own OpcodeList tags in one step — the typical workflow for inspecting a manufacturer's pipeline (e.g. DJI lens correction).
- **DNG colour transform**: applies AsShotNeutral white balance + ColorMatrix2 (D65, fallback ColorMatrix1) automatically when a DNG is open, so previews from camera DNGs come out roughly white-balanced instead of green-tinted. New "Apply DNG Color Transform" checkbox in the GUI. CLI gets a `--raw-colors` flag to skip it. Uses Bradford D50→D65 chromatic adaptation and a row-normalisation hack so the as-shot scene white maps exactly to (1,1,1) linear sRGB regardless of camera/calibration quirks.
- **FHD preview downsample**: full-resolution DNGs are kept on the side while a working copy (at most 1920x1080) feeds the opcode pipeline, so editing a 24 MP image stays interactive. New "Process at full resolution" checkbox bypasses the resize. CLI: `--max-dimension N` opt-in resize.
- `Core/ColorTransform.cs`: 3x3 matrix invert / multiply / apply helpers plus the camera-native-RGB to linear-sRGB builder.
- `Core/DngColorInfo.cs`: extracts AsShotNeutral + ColorMatrix1/2 from a DNG and pre-builds the camera-to-sRGB matrix.
- `PixelBuffer.Resize(maxW, maxH)`: parallel box-filter downsampler.
- `TiffFile.ReadEntryAsDoubleArray`: read RATIONAL / SRATIONAL / FLOAT / DOUBLE tag arrays at full precision (needed by AsShotNeutral / ColorMatrix).
- Tests: 7 new tests covering PixelBuffer.Resize (4) and ColorTransform (3 — identity, invert round-trip, white-maps-to-white).

### Notes

- The colour transform skips DNG's full tone-curve and chromatic adaptation between scene illuminant and calibration illuminant — it's a "good enough for preview" approximation. Slightly saturated colours can drift; whites are forced to (1,1,1).
- The FHD downsample uses absolute opcode coordinates as-is, which works for the common DJI case (GainMap covering the full image + radial WarpRectilinear) but may give approximate results for opcodes with sparse per-pixel data (DeltaPerRow / FixBadPixelsList). Enable "Process at full resolution" for exact output.

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

[0.8.7]: https://github.com/electro-logic/DngOpcodesEditor/compare/v0.8.6...v0.8.7
[0.8.6]: https://github.com/electro-logic/DngOpcodesEditor/compare/v0.8.5...v0.8.6
[0.8.5]: https://github.com/electro-logic/DngOpcodesEditor/compare/v0.8...v0.8.5
[0.8.0]: https://github.com/electro-logic/DngOpcodesEditor/compare/v0.7...v0.8
[0.7.0]: https://github.com/electro-logic/DngOpcodesEditor/compare/v0.6...v0.7
[0.6.0]: https://github.com/electro-logic/DngOpcodesEditor/releases/tag/v0.6
