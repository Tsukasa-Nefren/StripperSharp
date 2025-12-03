using System.Runtime.InteropServices;

namespace Kxnrl.StripperSharp.Natives;

[StructLayout(LayoutKind.Explicit, Pack = 8, Size = 8)]
internal unsafe struct CEntityLumpHandle
{
    [FieldOffset(0)]
    public CEntityLump* m_pLumpData;
}
