using System.Collections.Generic;

namespace Kxnrl.StripperSharp.Actions;

public class ModifyAction : BaseAction
{
    public List<ActionEntry> Matches { get; set; } = new();
    public List<ActionEntry> Replacements { get; set; } = new();
    public List<ActionEntry> Deletions { get; set; } = new();
    public List<ActionEntry> Insertions { get; set; } = new();

    public override ActionType GetType() => ActionType.Modify;
}
