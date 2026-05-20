# DNG opcode support and test-sample inventory

This document tracks every opcode defined by the [DNG 1.7.1 spec](specs/DNG_Spec_1_7_1_0.pdf) (Chapter 7 *Mapping Camera Color Space to CIE XYZ Space* and the opcode appendix), what the editor currently does with each, and where the testing gaps are.

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
|  9 | GainMap                 |  ✓   |   ✓   |    ✓    | Edge replication outside the map; bilinear interpolation between map points; multi-plane. |
| 10 | DeltaPerRow             |  ✓   |   ✓   |    ✓    | Float deltas added in normalised space; honours `top`/`rowPitch`. |
| 11 | DeltaPerColumn          |  ✓   |   ✓   |    ✓    | As above, columns. |
| 12 | ScalePerRow             |  ✓   |   ✓   |    ✓    | Multiplicative gain per row. |
| 13 | ScalePerColumn          |  ✓   |   ✓   |    ✓    | As above, columns. |
| 14 | WarpRectilinear2        |  ◐   |   ✗   |    ✗    | Tag id known; payload is read as raw bytes and round-trips as zeros. Introduced in DNG 1.6 to support per-channel rectilinear warps with a different parameterisation. |

## What's in the `Samples/` folder

The corpus is curated to **one DNG per `(camera model, opcode-list set)` combination** — duplicates from the original test sets live in `Samples/dng/extras/` and stay around in case a regression demands a wider sample (multiple frames from the same camera occasionally diverge in `FixBadPixelsList` content).

### Curated DNG samples

| File | Camera | Compression | Opcodes |
|---|---|---|---|
| `Samples/dng/20161129-DJI_0014.DNG` | DJI Phantom 4 (FC300C) | Uncompressed CFA, 4000×3000, 14-bit | OpcodeList1 — `FixBadPixelsList` |
| `Samples/dng/DJI_20230424201430_0069_D.DNG` | DJI Mavic 3 Pro (Hasselblad L2D-20c) | Lossless JPEG CFA, 5280×3956, 14-bit | OpcodeList3 — `GainMap` + `WarpRectilinear` |
| `Samples/dng/PXL_20211119_004121420.dng` | Google Pixel 6 | Lossless JPEG CFA | OpcodeList2 — 4× `GainMap` (per Bayer plane); OpcodeList3 — `WarpRectilinear` |
| `Samples/dng/IMG_5210.DNG` | Apple iPhone 15 Pro Max | Lossless JPEG **LinearRaw** | none |
| `Samples/dng/a0001-jmac_DSC1459.dng` | Nikon D70 | Lossless JPEG CFA | none (FiveK training DNG) |
| `Samples/dng/a0003-NKIM_MG_8178.dng` | Canon EOS 40D | Lossless JPEG CFA | none |
| `Samples/dng/a0009-kme_372.dng` | Fujifilm FinePix S2Pro | Lossless JPEG CFA | none |
| `Samples/dng/a0025-kme_298.dng` | Kodak DCS460 | Lossless JPEG CFA | none |
| `Samples/dng/a0074-WP_CRW_0343.dng` | Canon EOS 10D | Lossless JPEG CFA | none |
| `Samples/dng/a5000-kme_0204.dng` | Sony DSLR-A900 | Lossless JPEG CFA | none |

The Pixel 6 frame is the only sample in the set that carries an OpcodeList2 — and the 4× `GainMap` (one per Bayer plane) is a real-world workout for the GainMap implementation. The iPhone frame is the only **LinearRaw** sample (already demosaiced, photometric 34892).

### Other sample files

| File / kind | Contents | Covers opcodes |
|---|---|---|
| `Samples/WarpRectilinear.bin` | Single opcode payload | `WarpRectilinear` |
| `Samples/FixVignetteRadial.bin` | Single opcode payload | `FixVignetteRadial` |
| `Samples/GainMap.bin` | Single opcode payload | `GainMap` |
| `Samples/TrimsBound.bin` | Single opcode payload | `TrimBounds` |
| `Samples/grid.tiff`, `solid*.tiff`, `camera_070949.tiff` | LZW / Deflate RGB TIFFs | Not opcode samples — used by the TIFF reader / preview pipeline tests |
| `Samples/dng/extras/` | 27 duplicate DNGs (`20161129-DJI_0010.DNG`, the other 7 Mavic 3 Pro frames, and the `DNG for Yodani/` subfolder of 18 Phantom 4 frames with `.JPG` companions) | Same opcode sets as the curated DJI samples — kept for cases where shutter-count-varying `FixBadPixelsList` payloads matter |

So out of 14 spec opcodes, real DNG samples now cover **4** (`FixBadPixelsList`, `GainMap`, `WarpRectilinear`, plus the per-Bayer-plane `GainMap` chain on the Pixel 6) and synthetic `.bin` payloads cover **4** (`WarpRectilinear`, `FixVignetteRadial`, `GainMap`, `TrimBounds`).

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
| `WarpRectilinear2` | DNGs produced by recent Adobe profiles (DNG 1.6+) for multi-plane warps. |

A practical way to source these:

- **Adobe DNG Converter** with a deep camera profile (rare); the *Adobe DNG SDK* sample set bundled with `docs/specs/dng_sdk_1_7_1_2573_20260512.zip` ships small reference DNGs that may exercise some of these.
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

Outstanding (in priority order — see `CHANGELOG.md` and the spec analysis in `docs/specs/`):

1. **`ForwardMatrix1`/`2` (50964 / 50965)** — DNG-spec-preferred camera → XYZ path; behaves better at highlights than `inv(ColorMatrix)` and would further reduce residual colour drift.
2. **HueSatMap1 ↔ HueSatMap2 illuminant interpolation** — currently uses Map2 (D65) by default; the spec describes blending based on the AsShotNeutral CCT.
3. **`ProfileLookTable`** (50981 / 50982 / 51108) — "creative look", same HSV math as HueSatMap but applied later in the pipeline.
4. **`BaselineExposureOffset`** (51109) — trivial summation with `BaselineExposure`.
5. **`DefaultBlackRender`** (51110) — chooses between "auto blacks" and the profile's baked-in black point.
6. **`LinearResponseLimit`** (50734) — bound used to decide where to start highlight recovery; currently hard-coded to 0.95 in `ColorTransform`.
7. **`LinearizationTable`** (50712) — only relevant when a DNG ships one; none of the bundled samples do.
8. **`TransferFunction`** (TIFF tag 301) + embedded ICC profiles (tag 34675) — currently the input gamma decode defaults to sRGB EOTF; honouring an explicit per-file curve would handle AdobeRGB / ProPhoto / etc. TIFF inputs correctly.
