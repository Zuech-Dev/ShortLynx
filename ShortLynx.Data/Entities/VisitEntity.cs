using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

[Table("Visits")]
public class VisitEntity
{
    public Guid Id { get; set; }
    public Guid ShortCodeId { get; set; }
    public DateTimeOffset ClickedAt { get; set; }
    public string HashedIp { get; set; } = null!;
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }
    public virtual ShortCodeEntity ShortCode { get; set; } = null!;
}
