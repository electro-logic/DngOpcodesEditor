using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace DngOpcodesEditor
{
    public enum OpcodeId : uint
    {
        [Description("Correct radial and tangential distortions and lateral chromatic aberration for rectilinear lenses")]
        WarpRectilinear = 1,
        WarpFisheye = 2,
        [Description("Correct vignetting with a radially-symmetric gain function")]
        FixVignetteRadial = 3,
        FixBadPixelsConstant = 4,
        FixBadPixelsList = 5,
        [Description("Trims the image to the specified rectangle")]
        TrimBounds = 6,
        MapTable = 7,
        MapPolynomial = 8,
        [Description("Multiplies a specified area and plane range of an image by a gain map")]
        GainMap = 9,
        DeltaPerRow = 10,
        DeltaPerColumn = 11,
        ScalePerRow = 12,
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
        public OpcodeHeader header;
        // UI stuff
        [ObservableProperty]
        int listIndex = 3;
        [ObservableProperty]
        bool enabled = true;
        public List<OpcodeParameter> Parameters
        {
            get
            {
                var result = new List<OpcodeParameter>();
                foreach (var field in GetType().GetRuntimeFields())
                {
                    if (field.Name == "header")
                        continue;
                    if (field.FieldType.IsArray)
                    {
                        var array = field.GetValue(this) as Array;
                        for (int arrayIndex = 0; arrayIndex < array.Length; arrayIndex++)
                        {
                            var parameters = new OpcodeParameter
                            {
                                Description = $"{field.Name}[{arrayIndex}]",
                                Value = array.GetValue(arrayIndex),
                                FieldName = field.Name,
                                ArrayIndex = arrayIndex
                            };
                            parameters.PropertyChanged += (s, e) => {
                                try
                                {
                                    var p = s as OpcodeParameter;
                                    var field = GetType().GetField(p.FieldName);
                                    var array = field.GetValue(this) as Array;
                                    var arrayType = array.GetType().GetElementType();
                                    var newValue = Convert.ChangeType(p.Value, arrayType);
                                    array.SetValue(newValue, p.ArrayIndex);
                                    this.OnPropertyChanged(p.FieldName);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine(ex);
                                }
                            };
                            result.Add(parameters);
                        }
                    }
                    else
                    {
                        var parameters = new OpcodeParameter
                        {
                            Description = field.Name,
                            Value = field.GetValue(this),
                            FieldName = field.Name
                        };
                        parameters.PropertyChanged += (s,e)=> {
                            try
                            {
                                var p = s as OpcodeParameter;
                                var field = GetType().GetField(p.FieldName);
                                var newValue = Convert.ChangeType(p.Value, field.FieldType);
                                field.SetValue(this, newValue);
                                this.OnPropertyChanged(p.FieldName);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine(ex);
                            }
                        };
                        result.Add(parameters);
                    }
                }
                return result;
            }
        }        
        public override string ToString() => header.id.ToString();
        // End UI stuff
    }
    public class OpcodeHeader
    {
        public OpcodeId id;
        public DngVersion dngVersion;
        public OpcodeFlag flags;
        public UInt32 bytesCount;
    }
    public class OpcodeWarpRectilinear : Opcode
    {
        public UInt32 planes;
        public double[] coefficients;
        public double cx;
        public double cy;
    }
    public class OpcodeFixVignetteRadial : Opcode
    {
        public double k0;
        public double k1;
        public double k2;
        public double k3;
        public double k4;
        public double cx;
        public double cy;
    }
    public class OpcodeTrimBounds : Opcode
    {
        public UInt32 top;
        public UInt32 left;
        public UInt32 bottom;
        public UInt32 right;
    }
    public class OpcodeGainMap : Opcode
    {
        public UInt32 top;
        public UInt32 left;
        public UInt32 bottom;
        public UInt32 right;
        public UInt32 plane;
        public UInt32 planes;
        public UInt32 rowPitch;
        public UInt32 colPitch;
        public UInt32 mapPointsV;
        public UInt32 mapPointsH;
        public double mapSpacingV;
        public double mapSpacingH;
        public double mapOriginV;
        public double mapOriginH;
        public UInt32 mapPlanes;
        public float[] mapGains;
    }
}