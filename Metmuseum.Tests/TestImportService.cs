using Metmuseum.Data;
using Metmuseum.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Metmuseum.Tests
{
    public class TestImportService : ImportService
    {

        private readonly DbContextOptions<MetMuseumContext> _options;

        public TestImportService(IMetMuseumService metMuseumService,
                                 ILogger<ImportService> logger,
                                 DbContextOptions<MetMuseumContext> options)
            : base(metMuseumService, logger)
        {
            _options = options;
        }

        protected override MetMuseumContext CreateDbContext()
        {
            return new MetMuseumContext(_options);
        }
    }
}
