using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace DngOpcodesEditor
{
    public enum OpcodeId : uint
    {
        WarpRectilinear = 1,
        WarpFisheye = 2,
        FixVignetteRadial = 3,
        FixBadPixelsConstant = 4,
        FixBadPixelsList = 5,
        TrimBounds = 6,
        MapTable = 7,
        MapPolynomial = 8,
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
        DNG_VERSION_1_3_0_0 = 0x0103,
        DNG_VERSION_1_4_0_0 = 0x0104,
        DNG_VERSION_1_5_0_0 = 0x0105,
        DNG_VERSION_1_6_0_0 = 0x0106,
        DNG_VERSION_1_7_0_0 = 0x0107
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
    public class Opcode : ObservableObject
    {
        public OpcodeHeader header;
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

                                var p = s as OpcodeParameter;
                                var field = GetType().GetField(p.FieldName);
                                var array = field.GetValue(this) as Array;
                                var arrayType = array.GetType().GetElementType();
                                var newValue = Convert.ChangeType(p.Value, arrayType);
                                array.SetValue(newValue, p.ArrayIndex);
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

                            var p = s as OpcodeParameter;
                            var field = GetType().GetField(p.FieldName);
                            field.SetValue(this, Convert.ChangeType(p.Value, field.FieldType));
                        };
                        result.Add(parameters);
                    }
                }
                return result;
            }
        }
        public override string ToString() => header.id.ToString();
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
}