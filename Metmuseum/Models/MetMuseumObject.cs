using System.ComponentModel.DataAnnotations;

namespace Metmuseum.Models
{
    public class MetMuseumObject
    {
        [Key]
        public int ObjectID { get; set; }
        public string? Title { get; set; }
        public string JsonData { get; set; } = string.Empty;
        public DateTimeOffset RetrievedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
