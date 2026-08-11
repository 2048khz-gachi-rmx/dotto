using Dotto.Application.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dotto.Application;

public interface IDottoDbContext
{
    public DbSet<Entities.ChannelFlags> ChannelFlags { get; init; }
    public DbSet<DownloadedMediaRecord> DownloadedMedia { get; init; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}