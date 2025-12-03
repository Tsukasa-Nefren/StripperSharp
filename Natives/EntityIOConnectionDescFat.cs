using System.Runtime.InteropServices;
using Sharp.Shared.Utilities;

namespace Kxnrl.StripperSharp.Natives;

[StructLayout(LayoutKind.Explicit, Size = 64, Pack = 8)]
internal unsafe struct EntityIOConnectionDescFat
{
    [FieldOffset(0)]
    private byte* m_pszOutputName;

    [FieldOffset(8)]
    public EntityIOTargetType TargetType;

    [FieldOffset(16)]
    private byte* m_pszTargetName;

    [FieldOffset(24)]
    private byte* m_pszInputName;

    [FieldOffset(32)]
    private byte* m_pszOverrideParam;

    [FieldOffset(40)]
    public float Delay;

    [FieldOffset(44)]
    public int TimesToFire;

    [FieldOffset(48)]
    public CKeyValues3 KeyValues;

    public string OutputName    => NativeString.ReadString(m_pszOutputName);
    public string TargetName    => NativeString.ReadString(m_pszTargetName);
    public string InputName     => NativeString.ReadString(m_pszInputName);
    public string OverrideParam => NativeString.ReadString(m_pszOverrideParam);
}
