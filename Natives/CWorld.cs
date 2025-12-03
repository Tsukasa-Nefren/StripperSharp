using System.Runtime.InteropServices;
using Sharp.Shared.Types;
using Sharp.Shared.Types.Tier;

namespace Kxnrl.StripperSharp.Natives;

[StructLayout(LayoutKind.Explicit, Pack = 8, Size = 0x240)]
internal unsafe struct CWorld
{
    [FieldOffset(0x1E0)]
    public CUtlVector<Pointer<CEntityLumpHandle>> EntityLumps;
}
