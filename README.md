# Dng Opcodes Editor

Read, Write, Modify and Preview DNG Opcodes

![alt text](docs/screenshoot.png)

Opcodes parameters can be freely changed to see the effect on the image in real-time.

## Projects

The repo is organised as three projects sharing a common core:

- **Core** (`Core/DngOpcodesEditor.Core.csproj`) — platform-agnostic library:
  opcode reader/writer, opcode preview implementations, a built-in TIFF/DNG
  IFD parser, and a bilinear DNG raw demosaicer.
- **WPF** (`DngOpcodesEditor.csproj`) — original Windows front-end.
- **Avalonia** (`Avalonia/DngOpcodesEditor.Avalonia.csproj`) — cross-platform
  front-end (Windows, Linux, macOS).
- **Tests** (`Tests/DngOpcodesEditor.Tests.csproj`) — xUnit reader/writer
  round-trip and TIFF/raw tests.

## Supported opcodes (read, write and preview)

- WarpRectilinear (multi-plane, Brown-Conrady distortion model, bicubic resampling)
- FixVignetteRadial
- FixBadPixelsConstant
- FixBadPixelsList
- TrimBounds
- MapTable
- MapPolynomial
- GainMap
- DeltaPerRow / DeltaPerColumn
- ScalePerRow / ScalePerColumn

WarpFisheye is read and written but not previewed yet. FixBadPixels and the
region opcodes are designed for raw CFA data; on a demosaiced RGB preview they
are approximated.

## Required Software

- [.NET Desktop Runtime 9](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)

The previous external ExifTool dependency has been replaced with a built-in
TIFF/DNG IFD parser — no external binary is required to import or export
OpcodeList tags any more.

## Useful links

- [DNG 1.7.0.0 Specification](https://helpx.adobe.com/camera-raw/digital-negative.html)
- [LibRaw](https://www.libraw.org)

## Notes

- This project is not an official DNG Tool and may not be fully compliant with DNG Specifications.
- Open an issue if you need a specific opcode implemented.
- Import and Export cover OpcodeList1, OpcodeList2 and OpcodeList3. Each opcode keeps the list it belongs to and is exported back to the matching tag.
- FixVignetteRadial may require adjusting the strenght in some RAW processors (ex. Capture One).
- Opcode processing uses Span and .NET 9 and runs off the UI thread to keep the preview responsive.
- Reader/Writer round-trip tests live in the Tests project (run `dotnet test`).

## F.A.Q.

**- Can I open a DNG image directly?**

Yes for uncompressed strip-based DNGs — the editor includes a built-in CFA
reader with bilinear demosaic, so dropping a DNG opens it as the reference
image. Compressed DNGs (most modern files use Lossless JPEG) are not yet
supported; develop those into a linear TIFF first with LibRaw's `dcraw_emu`:

    dcraw_emu.exe -T -4 -o 0 input.DNG

The command produces a demosaiced linear TIFF image (16 bit) that can be opened
as a Reference Image.

**- Why the preview is too bright/dark?**

Because opcodes are designed to work before the gamma encoding.
If the reference image is gamma encoded check the "Decode Input Gamma" option, uncheck otherwise.
The "Encode Output Gamma" option should be always checked to properly display the preview image.

**- Why I can't see the preview image I saved?**

Ensure that your image viewer supports 16 bit TIFF files. The Avalonia front-end
currently saves PNG only.
