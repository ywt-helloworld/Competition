using MegaCrit.Sts2.Core.Entities.Multiplayer;

namespace Competition.CompetitionCode.Multiplayer;

public readonly record struct CompetitionHostResult(bool Succeeded, NetError? Error)
{
    public static CompetitionHostResult Success => new(true, null);

    public static CompetitionHostResult Failure(NetError error) => new(false, error);
}
