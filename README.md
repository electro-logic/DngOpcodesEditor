# Dng Opcodes Editor

Read, Write, Modify and Preview DNG Opcodes

![alt text](docs/screenshoot.png)

Opcodes parameters can be freely changed to see the effect on the image in real-time.

Supported opcodes:

- FixVignetteRadial
- WarpRectilinear (single plane only, based on the Brown-Conrady distortion model)
- TrimBounds
- GainMap (preliminary implementation)

Required Software:

- [.NET Desktop Runtime 6](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
- [ExifTool](https://exiftool.org)

Useful links:

- [DNG 1.7.0.0 Specification](https://helpx.adobe.com/camera-raw/digital-negative.html)
- [LibRaw](https://www.libraw.org)

Notes:

- This project is not an official DNG Tool and may not be fully compliant with DNG Specifications.
- Metadata reading/writing is based on ExifTool. Thank you Phil!
- Open an issue if you need a specific opcode implemented
- Export to DNG writes the OpcodeList3 tag only. You may need to write IFD0:OpcodeList3 if SubIFD is not defined in your DNG files.
- FixVignetteRadial may require adjusting the strenght in some RAW processors (ex. Capture One)

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