using System.Runtime.InteropServices;
using Sharp.Shared.Types;
using Sharp.Shared.Types.Tier;

namespace Kxnrl.StripperSharp.Natives;

[StructLayout(LayoutKind.Explicit, Size = 0x1238, Pack = 8)]
internal unsafe struct CEntityLump
{
    [FieldOffset(0)]
    public CUtlString pName;

    [FieldOffset(0x20)]
    public nint pAllocatorContext;

    [FieldOffset(0x1220)]
    public CUtlVector<Pointer<CEntityKeyValues>> EntityKeyValues;
}
