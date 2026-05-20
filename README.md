# Dng Opcodes Editor

Read, Write, Modify and Preview DNG Opcodes

![alt text](docs/screenshoot.png)

Opcodes parameters can be freely changed to see the effect on the image in real-time.

Supported opcodes (read, write and preview):

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

Required Software:

- [.NET Desktop Runtime 9](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- [ExifTool](https://exiftool.org)

Useful links:

- [DNG 1.7.0.0 Specification](https://helpx.adobe.com/camera-raw/digital-negative.html)
- [LibRaw](https://www.libraw.org)

Notes:

- This project is not an official DNG Tool and may not be fully compliant with DNG Specifications.
- Metadata reading/writing is based on ExifTool. Thank you Phil!
- Open an issue if you need a specific opcode implemented
- Import and Export cover OpcodeList1, OpcodeList2 and OpcodeList3. Each opcode keeps the list it belongs to and is exported back to the matching tag.
- FixVignetteRadial may require adjusting the strenght in some RAW processors (ex. Capture One)
- Opcode processing uses Span and .NET 9 and runs off the UI thread to keep the preview responsive.
- Reader/Writer round-trip tests live in the Tests project (run `dotnet test`).

F.A.Q:

**- Can I open a DNG image?**

No, you can only import Opcodes from a DNG file. To Open a DNG image, the file should be developed first.
You can develop the file in a minimal way by using the LibRaw utility dcraw_emu with the following command:

dcraw_emu.exe -T -4 -o 0 input.DNG

The command produces a demosaiced linear TIFF image (16 bit) that can be opened as a Reference Image.

**- Why the preview is too bright/dark?**

Because opcodes are designed to work before the gamma encoding.
If the reference image is gamma encoded check the "Decode Input Gamma" option, uncheck otherwise.
The "Encode Output Gamma" option should be always checked to properly display the preview image.

**- Why I can't see the preview image I saved?**

Ensure that your image viewer supports 16 bit TIFF files
