using KameronSushi.Application.Archives;

namespace KameronSushi.Application.Abstractions;

public interface IArchiveStore
{
    Task<IReadOnlyList<ArchiveMonth>> GetMonthsAsync(CancellationToken cancellationToken);
    Task<MonthlyDataArchive> BuildArchiveAsync(DateOnly periodStart, CancellationToken cancellationToken);
    Task RegisterPreparedArchiveAsync(DateOnly periodStart, string sha256, long sizeBytes, CancellationToken cancellationToken);
    Task<ArchivePurgeResult> ConfirmAndPurgeAsync(
        DateOnly periodStart, string sha256, long userId, CancellationToken cancellationToken);
}
