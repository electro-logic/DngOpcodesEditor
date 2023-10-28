# Dng Opcodes Editor

Read, Write and Preview DNG Opcodes

![alt text](docs/screenshoot.png)

Opcodes parameters can be freely changed to see the effect on the image in real-time.

Supported opcodes:

- FixVignetteRadial
- WarpRectilinear (single plane only, based on the Brown-Conrady distortion model)
- TrimBounds
- GainMap (preliminary implementation, not complete or correct)

Required Software:

- [.NET Desktop Runtime 6](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
- [ExifTool](https://exiftool.org)

Useful links:

- [DNG 1.7.0.0 Specification](https://helpx.adobe.com/camera-raw/digital-negative.html)

Notes:

- This project is not an official DNG Tool.
- Metadata reading/writing is based on ExifTool. Thank you Phil!
- Open an issue if you need a specific opcode implemented
- Export to DNG writes the OpcodeList3 tag only. You may need to write IFD0:OpcodeList3 in some cases
- TrimBounds may not be well supported by most RAW processors
- FixVignetteRadial may require adjusting the strenght in some RAW processors
- GainMap requires more testing