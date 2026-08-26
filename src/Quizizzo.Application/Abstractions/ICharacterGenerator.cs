using Quizizzo.Domain.Players;

namespace Quizizzo.Application.Abstractions;

public interface ICharacterGenerator
{
    CharacterDefinition Generate();
}
