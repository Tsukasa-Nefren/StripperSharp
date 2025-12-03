namespace Kxnrl.StripperSharp.Natives;

internal enum EntityIOTargetType : int
{
    Invalid               = -1,
    Classname             = 0,
    ClassnameDerivesFrom  = 1,
    EntityName            = 2,
    ContainsComponent     = 3,
    SpecialActivator      = 4,
    SpecialCaller         = 5,
    EntityHandle          = 6,
    EntityNameOrClassName = 7,
}
