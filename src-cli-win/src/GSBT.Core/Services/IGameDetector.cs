using GSBT.Core.Models;

namespace GSBT.Core.Services;

public interface IGameDetector
{
    Task<IReadOnlyList<GameRecord>> DetectAllGamesAsync(CancellationToken cancellationToken = default);
}
