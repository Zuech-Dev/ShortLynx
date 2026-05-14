using System.Collections.ObjectModel;

namespace ShortLynx.Data.Entities;

public class VisitEntity
{
    public Guid Id { get; set; }
    public Guid ShortCodeId { get; set; }
    public DateTimeOffset ClickedAt { get; set; }
    public string HashedIp { get; set; } = null!;
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }
    public virtual LinkEntity Link { get; set; } = null!;
}
