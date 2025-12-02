namespace Kxnrl.StripperSharp.Actions;

public enum ActionType
{
    Filter,
    Modify,
    Add
}

public abstract class BaseAction
{
    public new abstract ActionType GetType();
}
