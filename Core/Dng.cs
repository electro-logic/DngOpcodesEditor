using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace DngOpcodesEditor;

public enum OpcodeId : uint
{
    [Description("Correct radial and tangential distortions and lateral chromatic aberration for rectilinear lenses")]
    WarpRectilinear = 1,
    [Description("Correct distortions for fisheye lenses")]
    WarpFisheye = 2,
    [Description("Correct vignetting with a radially-symmetric gain function")]
    FixVignetteRadial = 3,
    [Description("Interpolate pixels matching a constant value from their neighbors")]
    FixBadPixelsConstant = 4,
    [Description("Interpolate a list of bad pixels and rectangles from their neighbors")]
    FixBadPixelsList = 5,
    [Description("Trims the image to the specified rectangle")]
    TrimBounds = 6,
    [Description("Applies a lookup table to a region and plane range of an image")]
    MapTable = 7,
    [Description("Applies a polynomial mapping to a region and plane range of an image")]
    MapPolynomial = 8,
    [Description("Multiplies a specified area and plane range of an image by a gain map")]
    GainMap = 9,
    [Description("Adds a per-row delta to a region and plane range of an image")]
    DeltaPerRow = 10,
    [Description("Adds a per-column delta to a region and plane range of an image")]
    DeltaPerColumn = 11,
    [Description("Multiplies each row of a region and plane range by a scale factor")]
    ScalePerRow = 12,
    [Description("Multiplies each column of a region and plane range by a scale factor")]
    ScalePerColumn = 13,
    WarpRectilinear2 = 14
}
public enum OpcodeFlag : uint
{
    Optional = 1,
    OptionalPreview = 2
}
public enum DngVersion : uint
{
    DNG_VERSION_1_3_0_0 = 0x01030000,
    DNG_VERSION_1_4_0_0 = 0x01040000,
    DNG_VERSION_1_5_0_0 = 0x01050000,
    DNG_VERSION_1_6_0_0 = 0x01060000,
    DNG_VERSION_1_7_0_0 = 0x01070000
}
public partial class OpcodeParameter : ObservableObject
{
    [ObservableProperty]
    string description;
    [ObservableProperty]
    object value;
    [ObservableProperty]
    string fieldName;
    [ObservableProperty]
    int arrayIndex;
}
public partial class Opcode : ObservableObject
{
    public OpcodeHeader header = new OpcodeHeader();
    // UI stuff
    [ObservableProperty]
    int listIndex = 3;
    [ObservableProperty]
    bool enabled = true;
    // The parameter list is built once and cached. Rebuilding it on every access
    // (as the WPF DataGrid does) would attach a new PropertyChanged handler each
    // time and cause a single edit to trigger ApplyOpcodes multiple times.
    List<OpcodeParameter> _parameters;
    public List<OpcodeParameter> Parameters => _parameters ??= BuildParameters();
    List<OpcodeParameter> BuildParameters()
    {
        var result = new List<OpcodeParameter>();
        foreach (var field in GetType().GetRuntimeFields())
        {
            // Only the public data fields of an opcode are editable parameters.
            // This skips the header, the UI-state fields (listIndex/enabled) and
            // any compiler/base-class generated fields.
            if (!field.IsPublic || field.Name == "header")
                continue;
            if (field.FieldType.IsArray)
            {
                var array = field.GetValue(this) as Array;
                for (int arrayIndex = 0; arrayIndex < array.Length; arrayIndex++)
                {
                    var parameter = new OpcodeParameter
                    {
                        Description = $"{field.Name}[{arrayIndex}]",
                        Value = array.GetValue(arrayIndex),
                        FieldName = field.Name,
                        ArrayIndex = arrayIndex
                    };
                    parameter.PropertyChanged += (s, e) =>
                    {
                        try
                        {
                            var p = s as OpcodeParameter;
                            var f = GetType().GetField(p.FieldName);
                            var arr = f.GetValue(this) as Array;
                            var elementType = arr.GetType().GetElementType();
                            arr.SetValue(Convert.ChangeType(p.Value, elementType), p.ArrayIndex);
                            OnPropertyChanged(p.FieldName);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex);
                        }
                    };
                    result.Add(parameter);
                }
            }
            else
            {
                var parameter = new OpcodeParameter
                {
                    Description = field.Name,
                    Value = field.GetValue(this),
                    FieldName = field.Name
                };
                parameter.PropertyChanged += (s, e) =>
                {
                    try
                    {
                        var p = s as OpcodeParameter;
                        var f = GetType().GetField(p.FieldName);
                        f.SetValue(this, Convert.ChangeType(p.Value, f.FieldType));
                        OnPropertyChanged(p.FieldName);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                };
                result.Add(parameter);
            }
        }
        return result;
    }
    public override string ToString() => header.id.ToString();
    // End UI stuff
}
public class OpcodeHeader
{
    public OpcodeId id;
    public DngVersion dngVersion = DngVersion.DNG_VERSION_1_3_0_0;
    public OpcodeFlag flags = OpcodeFlag.OptionalPreview;
    public UInt32 bytesCount;
}
public class OpcodeWarpRectilinear : Opcode
{
    public UInt32 planes;
    public double[] coefficients = new double[6];
    public double cx = 0.5;
    public double cy = 0.5;
}
public class OpcodeWarpFisheye : Opcode
{
    public UInt32 planes;
    public double[] coefficients = new double[4];
    public double cx = 0.5;
    public double cy = 0.5;
}
public class OpcodeFixVignetteRadial : Opcode
{
    public double k0;
    public double k1;
    public double k2;
    public double k3;
    public double k4;
    public double cx = 0.5;
    public double cy = 0.5;
}
public class OpcodeTrimBounds : Opcode
{
    public UInt32 top;
    public UInt32 left;
    public UInt32 bottom;
    public UInt32 right;
}
// Base class for the opcodes that operate on a rectangular region and a range
// of planes with a row/column pitch (GainMap, MapTable, MapPolynomial, Delta/Scale Per*).
public class OpcodeArea : Opcode
{
    public UInt32 top;
    public UInt32 left;
    public UInt32 bottom;
    public UInt32 right;
    public UInt32 plane;
    public UInt32 planes = 1;
    public UInt32 rowPitch = 1;
    public UInt32 colPitch = 1;
}
public class OpcodeGainMap : OpcodeArea
{
    public UInt32 mapPointsV;
    public UInt32 mapPointsH;
    public double mapSpacingV;
    public double mapSpacingH;
    public double mapOriginV;
    public double mapOriginH;
    public UInt32 mapPlanes;
    public float[] mapGains = Array.Empty<float>();
}
public class OpcodeMapTable : OpcodeArea
{
    public UInt16[] table = Array.Empty<UInt16>();
}
public class OpcodeMapPolynomial : OpcodeArea
{
    public double[] coefficients = new double[] { 0.0, 1.0 };
}
public class OpcodeDeltaPerRow : OpcodeArea
{
    public float[] deltas = Array.Empty<float>();
}
public class OpcodeDeltaPerColumn : OpcodeArea
{
    public float[] deltas = Array.Empty<float>();
}
public class OpcodeScalePerRow : OpcodeArea
{
    public float[] scales = Array.Empty<float>();
}
public class OpcodeScalePerColumn : OpcodeArea
{
    public float[] scales = Array.Empty<float>();
}
public class OpcodeFixBadPixelsConstant : Opcode
{
    public UInt32 constant;
    public UInt32 bayerPhase;
}
public class OpcodeFixBadPixelsList : Opcode
{
    public UInt32 bayerPhase;
    // Flat row/col pairs.
    public UInt32[] badPoints = Array.Empty<UInt32>();
    // Flat top/left/bottom/right quads.
    public UInt32[] badRects = Array.Empty<UInt32>();
}
