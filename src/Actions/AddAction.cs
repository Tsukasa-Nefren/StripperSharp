using System.Collections.Generic;

namespace Kxnrl.StripperSharp.Actions;

public class AddAction : BaseAction
{
    public List<ActionEntry> Insertions { get; set; } = new();

    public override ActionType GetType() => ActionType.Add;
}
