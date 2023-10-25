# Dng Opcodes Editor

Read, Write and Preview DNG Opcodes

![alt text](docs/screenshoot.png)

Opcodes parameters can be freely changed to see the effect on the image in real-time.

Supported opcodes:

- FixVignetteRadial
- WarpRectilinear (single plane only, based on the Brown-Conrady distortion model)
- TrimBounds

Required Software:

- Microsoft Visual Studio 2022 (WPF / .NET 6)

Useful links:

- [DNG 1.7.0.0 Specification](https://helpx.adobe.com/camera-raw/digital-negative.html)
- [ExifTool](https://exiftool.org)

Notes:

- This project is not an official DNG Tool.
- Metadata reading/writing is based on ExifTool. Thank you Phil!
- Open an issue if you need a specific opcode implemented
- Export to DNG writes the OpcodeList3 tag only. You may need to write IFD0:OpcodeList3 in some cases
- TrimBounds may not be well supported by most RAW processors
- FixVignetteRadial may require adjusting the strenght in some RAW processors