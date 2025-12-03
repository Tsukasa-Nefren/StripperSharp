using System.Runtime.InteropServices;
using Sharp.Shared.Types.Tier;

namespace Kxnrl.StripperSharp.Natives;

[StructLayout(LayoutKind.Explicit, Size = 56)]
internal unsafe struct CSingleWorldRep
{
    [FieldOffset(0)]
    private void* pVTable;

    [FieldOffset(8)]
    public CUtlString Name;

    [FieldOffset(48)]
    public CWorld* pWorld;
}
