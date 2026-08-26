using Quizizzo.Domain.Parties;

namespace Quizizzo.Application.Abstractions;

public interface IRoomCodeGenerator
{
    RoomCode Generate();
}
