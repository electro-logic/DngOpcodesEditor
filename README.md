# DNG Opcodes Editor

Read, write, edit and preview DNG opcodes with live feedback.

![DNG Opcodes Editor](docs/screenshoot.png)

Adjust any opcode parameter with a slider and watch the preview image update in real time. Import opcodes from a DNG file or a raw `.bin` payload, edit them, then export back to either format.

---

## Features

- **Live preview** of the opcode chain with multi-threaded pixel processing that runs off the UI thread.
- **Full coverage** of OpcodeList1, OpcodeList2 and OpcodeList3 — each opcode remembers the list it belongs to and is written back to the matching tag.
- **No external dependencies** — a built-in TIFF/DNG IFD parser replaces the previous ExifTool requirement.
- **Direct DNG open** for uncompressed CFA DNGs via a built-in bilinear demosaic (no `dcraw_emu` step needed).
- **Drag-and-drop** any combination of `.tiff`, `.dng` and `.bin` files onto the window.
- **Per-opcode enable / disable**, gamma encode/decode toggles, and an "Add Opcode" picker.
- **Decoupled Core library** — the WPF window is a thin shell on top of a platform-agnostic core.

## Supported opcodes

| ID | Opcode                  | Read | Write | Preview |
|----|-------------------------|:----:|:-----:|:-------:|
| 1  | WarpRectilinear         |  ✓   |   ✓   |    ✓    |
| 2  | WarpFisheye             |  ✓   |   ✓   |         |
| 3  | FixVignetteRadial       |  ✓   |   ✓   |    ✓    |
| 4  | FixBadPixelsConstant    |  ✓   |   ✓   |    ✓    |
| 5  | FixBadPixelsList        |  ✓   |   ✓   |    ✓    |
| 6  | TrimBounds              |  ✓   |   ✓   |    ✓    |
| 7  | MapTable                |  ✓   |   ✓   |    ✓    |
| 8  | MapPolynomial           |  ✓   |   ✓   |    ✓    |
| 9  | GainMap                 |  ✓   |   ✓   |    ✓    |
| 10 | DeltaPerRow             |  ✓   |   ✓   |    ✓    |
| 11 | DeltaPerColumn          |  ✓   |   ✓   |    ✓    |
| 12 | ScalePerRow             |  ✓   |   ✓   |    ✓    |
| 13 | ScalePerColumn          |  ✓   |   ✓   |    ✓    |

`WarpRectilinear` supports multi-plane warps (chromatic-aberration correction) and bicubic resampling.

`FixBadPixels*` and the region opcodes are designed for raw CFA data; on a demosaiced RGB preview they are approximated.

## Getting started

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) on Windows.

```
git clone <repo>
cd DngOpcodesEditor
dotnet run --project DngOpcodesEditor.csproj
```

Run the test suite with:

```
dotnet test
```

## Project layout

| Project                                 | Purpose                                                                                  |
|-----------------------------------------|------------------------------------------------------------------------------------------|
| `Core/DngOpcodesEditor.Core.csproj`     | Platform-agnostic library: opcode reader/writer, opcode preview implementations, TIFF/DNG IFD parser and bilinear DNG raw demosaicer. |
| `DngOpcodesEditor.csproj`               | WPF (Windows) front-end. Thin layer on top of Core.                                      |
| `Tests/DngOpcodesEditor.Tests.csproj`   | xUnit round-trip and TIFF/raw tests.                                                     |

## FAQ

### Can I open a DNG image directly?

Yes for **uncompressed strip-based DNGs** — the editor includes a built-in CFA reader with bilinear demosaic, so dropping a DNG opens it as the reference image.

Compressed DNGs (most modern files use Lossless JPEG) are not yet supported; develop those into a linear TIFF first with LibRaw's `dcraw_emu`:

```
dcraw_emu.exe -T -4 -o 0 input.DNG
```

The command produces a demosaiced linear TIFF image (16 bit) that can be opened as a Reference Image.

### Why is the preview too bright or too dark?

Opcodes are designed to work on linear, pre-gamma data. If your reference image is gamma-encoded (most 8-bit TIFFs and PNGs), tick **Decode Input Gamma**; leave it unticked for linear input. **Encode Output Gamma** should normally stay ticked so the preview displays correctly on screen.

### Why can't I see the preview image I saved?

Make sure your image viewer supports 16-bit TIFF files.

## Notes

- This is not an official DNG tool and may not be fully compliant with the DNG specification.
- `FixVignetteRadial` may require adjusting the strength in some RAW processors (for example Capture One).
- Open an issue if you need an opcode that is not yet implemented.

## Links

- [DNG 1.7.0.0 Specification](https://helpx.adobe.com/camera-raw/digital-negative.html)
- [LibRaw](https://www.libraw.org)
- [Changelog](CHANGELOG.md)
