using System.Collections.Generic;

namespace Kxnrl.StripperSharp.Actions;

public class FilterAction : BaseAction
{
    public List<ActionEntry> Matches { get; set; } = new();

    public override ActionType GetType() => ActionType.Filter;
}
