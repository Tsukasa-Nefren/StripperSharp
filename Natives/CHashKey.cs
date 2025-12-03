using System.Runtime.InteropServices;

namespace Kxnrl.StripperSharp.Natives;

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal unsafe struct CHashKey
{
    [FieldOffset(0)]
    public uint HashCode;

    [FieldOffset(8)]
    public byte* KeyPointer;
}
