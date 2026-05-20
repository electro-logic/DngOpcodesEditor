# DNG opcode support and test-sample inventory

This document tracks every opcode defined by the [DNG 1.7.1 spec](https://helpx.adobe.com/camera-raw/digital-negative.html) (Chapter 7 *Mapping Camera Color Space to CIE XYZ Space* and the opcode appendix), what the editor currently does with each, and where the testing gaps are.

| Column | Meaning |
|---|---|
| **Read** | `OpcodesReader` parses the opcode's binary layout into a typed CLR object. |
| **Write** | `OpcodesWriter` round-trips the typed object back to bytes. |
| **Preview** | `OpcodesImplementation.Apply` runs the opcode on a `PixelBuffer` and the result is visible in the WPF preview / CLI `preview` output. |

Status legend: ✓ supported · ◐ partial (see *Notes*) · ✗ not implemented.

Each opcode lives in its own file under `Core/Opcodes/<Name>.cs` (parts of a single `partial class OpcodesImplementation`). Each file opens with a doc-comment block explaining the opcode's math, parameters, typical OpcodeList placement and approximation notes.

## Coverage matrix

| ID | Opcode                  | Read | Write | Preview | Notes |
|----|-------------------------|:----:|:-----:|:-------:|-------|
|  1 | WarpRectilinear         |  ✓   |   ✓   |    ✓    | Multi-plane (chromatic aberration), Brown–Conrady distortion model, bicubic resampling. |
|  2 | WarpFisheye             |  ✓   |   ✓   |    ✓    | Per-plane 4-coefficient polynomial in `atan(r)` with bicubic backward sampling. |
|  3 | FixVignetteRadial       |  ✓   |   ✓   |    ✓    | Polynomial-in-`r²` gain function. |
|  4 | FixBadPixelsConstant    |  ✓   |   ✓   |    ◐    | Designed for raw CFA data; on the demosaiced RGB preview pixels matching the constant are replaced by 4-neighbour averages — an approximation. |
|  5 | FixBadPixelsList        |  ✓   |   ✓   |    ◐    | Same approximation as above (CFA tag applied on RGB). |
|  6 | TrimBounds              |  ✓   |   ✓   |    ✓    | Trimmed pixels are masked to black (no resize). |
|  7 | MapTable                |  ✓   |   ✓   |    ✓    | 16-bit LUT; honours region, plane range and row/col pitch. |
|  8 | MapPolynomial           |  ✓   |   ✓   |    ✓    | Polynomial of arbitrary degree in normalised `[0,1]` space. |
|  9 | GainMap                 |  ✓   |   ✓   |    ✓    | Edge replication outside the map; bilinear interpolation between map points; multi-plane. **OpcodeList2 GainMaps** (per-Bayer-plane shading correction) are applied to the linearised CFA buffer before demosaicing — toggling them in the UI does not affect the preview without reloading the file. OpcodeList3 GainMaps (multi-plane on RGB) remain interactive. |
| 10 | DeltaPerRow             |  ✓   |   ✓   |    ✓    | Float deltas added in normalised space; honours `top`/`rowPitch`. |
| 11 | DeltaPerColumn          |  ✓   |   ✓   |    ✓    | As above, columns. |
| 12 | ScalePerRow             |  ✓   |   ✓   |    ✓    | Multiplicative gain per row. |
| 13 | ScalePerColumn          |  ✓   |   ✓   |    ✓    | As above, columns. |
| 14 | WarpRectilinear2        |  ✓   |   ✓   |    ✓    | DNG 1.6 per-channel rectilinear warp. Up-to-order-14 radial polynomial with both odd & even powers, optional valid-radius clamp, optional reciprocal-radial mode. Skip-rule honoured (an optional WR2 silences the next WarpRectilinear / WarpFisheye in the same list). |

## What's in the `Samples/` folder

The corpus is curated to **only DNGs that actually ship OpcodeList tags** — one per `(camera model, opcode-list set)` combination. DNGs without opcodes (the FiveK CFA training set, the iPhone LinearRaw sample, and duplicate frames from the same camera) live in `Samples/dng/extras/` and remain available when a regression demands a wider sample.

### Curated DNG samples

| File | Camera | Compression | Opcodes |
|---|---|---|---|
| `Samples/dng/20161129-DJI_0014.DNG` | DJI Phantom 4 (FC300C) | Uncompressed CFA, 4000×3000, 14-bit | OpcodeList1 — `FixBadPixelsList` |
| `Samples/dng/DJI_20230424201430_0069_D.DNG` | DJI Mavic 3 Pro (Hasselblad L2D-20c) | Lossless JPEG CFA, 5280×3956, 14-bit | OpcodeList3 — `GainMap` + `WarpRectilinear` |
| `Samples/dng/PXL_20211119_004121420.dng` | Google Pixel 6 | Lossless JPEG CFA | OpcodeList2 — 4× `GainMap` (per Bayer plane); OpcodeList3 — `WarpRectilinear` |

The Pixel 6 frame is the only sample in the set that carries an OpcodeList2 — and the 4× `GainMap` (one per Bayer plane) is a real-world workout for the CFA-stage GainMap implementation.

### Other sample files

| File / kind | Contents | Covers opcodes |
|---|---|---|
| `Samples/WarpRectilinear.bin` | Single opcode payload | `WarpRectilinear` |
| `Samples/FixVignetteRadial.bin` | Single opcode payload | `FixVignetteRadial` |
| `Samples/GainMap.bin` | Single opcode payload | `GainMap` |
| `Samples/TrimsBound.bin` | Single opcode payload | `TrimBounds` |
| `Samples/grid.tiff`, `solid*.tiff`, `camera_070949.tiff` | LZW / Deflate RGB TIFFs | Not opcode samples — used by the TIFF reader / preview pipeline tests |
| `Samples/dng/extras/IMG_5210.DNG` | Apple iPhone 15 Pro Max — the only **LinearRaw** sample in the collection (photometric 34892, already demosaiced) | none |
| `Samples/dng/extras/a*.dng` (6 files) | FiveK CFA training DNGs — Nikon D70, Canon EOS 40D, Canon EOS 10D, Fujifilm FinePix S2Pro, Kodak DCS460, Sony DSLR-A900. Useful for testing the CFA reader and colour transform without opcodes. | none |
| `Samples/dng/extras/` (DJI duplicates) | `20161129-DJI_0010.DNG`, the other 7 Mavic 3 Pro frames, and the `DNG for Yodani/` subfolder of 18 Phantom 4 frames with `.JPG` companions | Same opcode sets as the curated DJI samples — kept for cases where shutter-count-varying `FixBadPixelsList` payloads matter |

So out of 14 spec opcodes, real DNG samples cover **4** (`FixBadPixelsList`, `GainMap`, `WarpRectilinear`, plus the per-Bayer-plane `GainMap` chain on the Pixel 6) and synthetic `.bin` payloads cover **4** (`WarpRectilinear`, `FixVignetteRadial`, `GainMap`, `TrimBounds`).

## Where we still need real test material

These are opcodes the editor can read / write / preview, but for which we have **no** real-world camera DNG to validate the implementation against:

| Opcode | Where you'd typically find one in the wild |
|---|---|
| `WarpFisheye` | Action / 360 cameras (some GoPro firmwares, Insta360 raws). Drone wide-angle modes that ship fisheye correction inside the DNG. |
| `FixBadPixelsConstant` | Cameras that flag sensor "stuck pixels" with a sentinel value. Industrial / scientific raw output. Less common in consumer raws. |
| `MapTable` | Adobe DNG Converter when applied with a calibration profile that uses a per-channel lookup. Some Foveon / Sigma DNGs. |
| `MapPolynomial` | Same converters; sometimes used for monochrome / scientific cameras. |
| `DeltaPerRow` / `DeltaPerColumn` | Sensor-row banding correction. Common in dark-frame-calibrated astrophotography DNGs. |
| `ScalePerRow` / `ScalePerColumn` | Per-row PRNU correction. Astro / industrial. |

A practical way to source these:

- **Adobe DNG Converter** with a deep camera profile (rare); the *Adobe DNG SDK* sample set (downloadable from Adobe) ships small reference DNGs that may exercise some of these.
- **The MIT-Adobe FiveK dataset** ([data.csail.mit.edu/graphics/fivek](https://data.csail.mit.edu/graphics/fivek/)) — large corpus of real-world DNGs across many cameras; a few include the rarer opcode lists.
- **OpenAstroProject / N.I.N.A. forums** — published calibration DNGs often contain `DeltaPerRow` / `ScalePerColumn`.
- **DJI / Skydio / Parrot firmware updates** sometimes change which opcodes ship in the DNG; collecting frames from new firmware can surface `WarpFisheye` and `WarpRectilinear2`.
- **Synthetic generation** — for any opcode the editor can already *write*, we can hand-construct a `.bin` payload and unit-test the round-trip + preview behaviour. The existing `Tests/OpcodesRoundTripTests.cs` already does this for `MapTable`, `MapPolynomial`, `DeltaPerRow`, `GainMap`, etc., so the parsing is exercised even without real DNGs — but real frames are still needed to validate visual correctness.

## What's *not* yet supported from the DNG colour-rendering stack

The opcode list isn't the only metadata that drives a faithful preview. The colour-rendering pipeline currently honours, in pipeline order:

1. `BlackLevel` (50714) + `WhiteLevel` (50717) — sample linearisation
2. `CFAPattern` (33422) — bilinear demosaic
3. `Orientation` (274) — rotate the image to its EXIF orientation
4. (OpcodeList1 / 2 / 3 — applied here against camera-native RGB)
5. `AsShotNeutral` (50728) + `ColorMatrix2` (50722, fallback `ColorMatrix1` 50721) — DNG-spec WB diagonal: `referenceNeutral = M·D50_white`, `D[i] = referenceNeutral[i]/AsShotNeutral[i]`, then `camera→sRGB = XyzToSrgb_D65 · Bradford_D50→D65 · inv(M) · diag(D)` (replaces a previous post-hoc row-normalisation that distorted blues)
6. Highlight desaturation — blend toward neutral when any WB'd channel exceeds 0.95
7. `BaselineExposure` (50730) — uniform `2^stops` gain
8. `ProfileHueSatMap` (50937 + 50939, falls back to 50938) — per-hue (hueShift°, satScale, valScale) tweaks via trilinear HSV interpolation
9. `ProfileToneCurve` (50940) — per-channel tone curve via 4096-entry LUT
10. **sRGB OETF** (IEC 61966-2-1, not `pow(c, 1/2.2)`) — display encoding
11. **TPDF dither** at the 16→8 bit display conversion (WPF preview only; saved 16-bit TIFFs are untouched)

Outstanding (in priority order — see `CHANGELOG.md` for history):

1. **`ForwardMatrix1`/`2` (50964 / 50965)** — DNG-spec-preferred camera → XYZ path; behaves better at highlights than `inv(ColorMatrix)` and would further reduce residual colour drift.
2. **HueSatMap1 ↔ HueSatMap2 illuminant interpolation** — currently uses Map2 (D65) by default; the spec describes blending based on the AsShotNeutral CCT.
3. **`ProfileLookTable`** (50981 / 50982 / 51108) — "creative look", same HSV math as HueSatMap but applied later in the pipeline.
4. **`BaselineExposureOffset`** (51109) — trivial summation with `BaselineExposure`.
5. **`DefaultBlackRender`** (51110) — chooses between "auto blacks" and the profile's baked-in black point.
6. **`LinearResponseLimit`** (50734) — bound used to decide where to start highlight recovery; currently hard-coded to 0.95 in `ColorTransform`.
7. **`LinearizationTable`** (50712) — only relevant when a DNG ships one; none of the bundled samples do.
8. **`TransferFunction`** (TIFF tag 301) + embedded ICC profiles (tag 34675) — currently the input gamma decode defaults to sRGB EOTF; honouring an explicit per-file curve would handle AdobeRGB / ProPhoto / etc. TIFF inputs correctly.

## Roadmap — what to tackle next

A broader picture across opcodes, infrastructure, display, and UX. Items are roughly ordered by impact ÷ effort; pick whichever scratches the itch you have.

### Opcode completeness

1. ~~`WarpRectilinear2` (id 14)~~ — **Done in 0.9.1.** DNG 1.6 per-channel rectilinear warp, up-to-order-14 radial polynomial (odd + even powers), optional reciprocal-radial mode, plus the spec's "skip rule" (an optional WR2 silences the next WarpRectilinear / WarpFisheye in the same list).
2. ~~OpcodeList2 on CFA — opcodes beyond `GainMap`~~ — **Done in 0.9.1.** `DngRawReader` now applies the full set of L2-eligible opcodes (`MapTable`, `MapPolynomial`, `Delta{Row,Col}`, `Scale{Row,Col}`, `FixBadPixels{Constant,List}`) to the linearised CFA before demosaicing — same dispatch path as `GainMap`. Implementations live in `Core/DngRawReader.L2OnCfa.cs`. `FixBadPixels*` on CFA uses same-Bayer-colour neighbours (±2 pixels) instead of the 4-connected average, matching how the spec intends it. No real-world samples exercise these yet, so they're pinned by synthetic tests in `Tests/OpcodeList2CfaOpcodesTests.cs` (11 cases).
3. ~~Live editing of L2 opcodes affects the preview~~ — **Done in 0.9.1.** `MainWindowVM` now caches the raw DNG bytes at load time (`_originalDngBytes`); editing or toggling an L2 opcode sets an `_l2Dirty` flag that triggers a re-decode at the next `ApplyOpcodes` pass via the new `DngRawReader.Read(bytes, l2Override)` overload. Re-decode runs off the UI thread; the existing `_applyPending` coalesce handles rapid slider edits so we don't queue multiple re-decodes. The override is filtered to `ListIndex == 2 && Enabled`, so disabling an L2 opcode in the editor genuinely skips it on next preview.

### Colour pipeline

See the *Outstanding* list above — `ForwardMatrix1/2` is the highest-impact item there: it directly affects highlight rendering on every DNG, not just edge cases.

### Test / build infrastructure

4. **Convert sample fixtures to embedded test resources.** `LzwDecoderTests.OpensLzwCompressedTiffSample`, `DeflateDecoderTests` (`solid64.tiff`) and the four `OpcodesRoundTripTests` `[InlineData]` cases all read from `AppContext.BaseDirectory/Samples/`. Since 0.9.0 untracked `Samples/`, these tests pass only because of stale `Tests/bin/Debug/*` copies surviving from earlier builds — `dotnet clean` followed by a fresh build will cause every one of them to fail. The fix is small: move the four `.bin` payloads + two `.tiff` files into `Tests/Fixtures/` (or similar), mark them `<EmbeddedResource>`, and load via `Assembly.GetManifestResourceStream`. ~12 KB of binary additions to the repo, but they're test-essential, not user-facing samples.
5. **CI on a Windows runner.** The project is .NET 9 + WPF — Linux GitHub Actions can build `Core/` and run tests, but not the WPF app. A `windows-latest` workflow that does `dotnet build`, `dotnet test` and uploads the WPF binary as a release artefact would catch the kind of test-fragility above on every push.

### Display / colour management

6. **HDR display path on Windows 11.** The pipeline already keeps a 16-bit-per-channel linear buffer all the way through `Image.Update`; the final stop is a TPDF-dithered 16→8 conversion to `Bgr32`. On an HDR-capable monitor with the OS in HDR mode, WPF's compositor accepts a higher-bit-depth surface via `PixelFormats.Rgba128Float` (or `Rgba64`) and the DWM will pipe extended-range values through without tone-mapping. Switching `_bmpDisplay` to a float / 16-bit surface conditionally (e.g. detect via `WindowInteropHelper` + DXGI swap-chain HDR query) would let users see the linear pipeline without the 8-bit display crush. Worth a checkbox + a clear *"HDR monitor required"* tooltip.
7. **`TransferFunction` + embedded ICC profile parsing for TIFF inputs** (also item #8 in the colour-pipeline list) — currently the only input-gamma options are "treat as linear" or "treat as sRGB". Honouring TIFF tag 301 and tag 34675 would correctly decode AdobeRGB, ProPhoto and the various wide-gamut TIFFs that come out of editing software.

### UX / editor polish

8. **Histogram view.** A small luminance / per-channel histogram of `ImgDst` (under the action buttons or in a new tab) would make the effect of every opcode change obvious — especially exposure and tone curves.
9. **Side-by-side compare against a reference DNG.** The editor already loads one reference image (left) + one processed (right). A "load reference DNG for comparison" command that puts an *independently developed* image (e.g. an Adobe-rendered TIFF) into the right slot would make rendering-fidelity work much easier.
10. **Better array-parameter editing.** Opcodes like `WarpRectilinear` (`coefficients[0..5]`) and `GainMap` (the entire `mapGains` array) are tedious to edit one slider at a time. A "load coefficients from text" or "paste JSON" path would help when tracking down a specific bug.

### Performance

11. **SIMD / `Vector<T>` in the hot opcode loops.** The bicubic resampler in `WarpRectilinear` / `WarpFisheye` and the inner channel loops in `GainMap` / `MapPolynomial` / `Scale*` / `Delta*` are all embarrassingly parallel per pixel. The current `Parallel.For` over rows uses one float multiply-add per channel — switching to `Vector256<float>` or `System.Numerics.Vector<float>` would 4–8× the throughput on those stages. Worth doing *after* the test fixtures are bulletproof (item 4) so the perf work doesn't risk silent regressions.
