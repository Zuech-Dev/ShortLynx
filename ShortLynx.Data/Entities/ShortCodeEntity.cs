namespace ShortLynx.Data.Entities;

public class ShortCodeEntity
{
    public Guid Id { get; set; }
    public Guid LinkId { get; set; }
    public string Code { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
