using System;
using System.Runtime.CompilerServices;

namespace DngOpcodesEditor;

public static class UnsafeArray
{
    // http://blogs.ugidotnet.org/leonardo/archive/2023/11/24/water-in-wine-csharp-array-cast.aspx
    static unsafe void* GetPointer<T>(ref T value)
    {
        return *(void**)Unsafe.AsPointer(ref value);
    }
    static unsafe IntPtr GetMethodTable<T>(ref T value)
    {
        return Unsafe.Read<IntPtr>(GetPointer(ref value));
    }
    public static unsafe toK[] CastArray<fromT,toK>(fromT[] array)
    {
        var targetType = new toK[0];
        var arrayPtr = GetPointer(ref array);
        Unsafe.Write<IntPtr>(arrayPtr, GetMethodTable(ref targetType));
        arrayPtr = Unsafe.Add<IntPtr>(arrayPtr, 1);
        var arraySize = Unsafe.ReadUnaligned<UInt32>(arrayPtr);
        Unsafe.WriteUnaligned<UInt32>(arrayPtr, (UInt32)(arraySize*sizeof(fromT)/sizeof(toK)));
        return Unsafe.As<toK[]>(array);
    }
}
