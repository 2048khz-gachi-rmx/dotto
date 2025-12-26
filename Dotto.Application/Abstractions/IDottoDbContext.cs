using Dotto.Application.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dotto.Application.Abstractions;

public interface IDottoDbContext
{
    public DbSet<ChannelFlags> ChannelFlags { get; init; }
    public DbSet<DownloadedMediaRecord> DownloadedMedia { get; init; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}