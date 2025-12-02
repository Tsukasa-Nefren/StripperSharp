using System;
using System.Text.RegularExpressions;

namespace Kxnrl.StripperSharp.Actions;

public class ActionEntry
{
    public string Name { get; set; } = "";
    public ActionValue Value { get; set; } = new ActionValue();
}

public class ActionValue
{
    public string? StringValue { get; set; }
    public Regex? RegexValue { get; set; }
    public IOConnection? IOValue { get; set; }

    public bool IsString => StringValue != null;
    public bool IsRegex => RegexValue != null;
    public bool IsIO => IOValue != null;
    public bool IsEmpty => StringValue == null && RegexValue == null && IOValue == null;
}

public class IOConnection
{
    public ActionValue OutputName { get; set; } = new();
    public ActionValue? TargetName { get; set; }
    public ActionValue? InputName { get; set; }
    public ActionValue? OverrideParam { get; set; }
    public float? Delay { get; set; }
    public int? TimesToFire { get; set; }
    public int? TargetType { get; set; }
}
