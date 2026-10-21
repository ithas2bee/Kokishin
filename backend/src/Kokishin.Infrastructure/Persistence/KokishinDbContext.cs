using Microsoft.EntityFrameworkCore;

namespace Kokishin.Infrastructure.Persistence;

public sealed class KokishinDbContext(DbContextOptions<KokishinDbContext> options) : DbContext(options)
{
}
