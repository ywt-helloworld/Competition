using MegaCrit.Sts2.Core.Models;

namespace Competition.CompetitionCode.Integration;

/// <summary>
/// Provides the stable, built-in character pool used by Competition at match
/// start. ModelDb.AllCharacters is the original game's five base characters,
/// so no mod character or localized display name crosses the network.
/// </summary>
public static class CompetitionCharacterProvider
{
    public static CharacterModel GetRandomSharedBaseCharacter()
    {
        CharacterModel[] candidates = ModelDb.AllCharacters.ToArray();
        return candidates[Random.Shared.Next(candidates.Length)];
    }

    public static CharacterModel? ResolveSharedBaseCharacter(string serializedModelId)
    {
        try
        {
            CharacterModel? character = ModelDb.GetByIdOrNull<CharacterModel>(ModelId.Deserialize(serializedModelId));
            return character != null && ModelDb.AllCharacters.Any(candidate => candidate.Id == character.Id)
                ? character
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
