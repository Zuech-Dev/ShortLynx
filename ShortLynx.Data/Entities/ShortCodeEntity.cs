using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

[Table("ShortCodes")]
public class ShortCodeEntity
{
    public Guid Id { get; set; }
    public Guid LinkId { get; set; }
    public string Code { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// True when the operator chose the code (vanity code), false for a generated one. Custom codes
    /// resolve under the dedicated custom route prefix (default <c>/c/</c>), never the root <c>/{code}</c>;
    /// the redirect handlers filter on this so the two namespaces stay separate.
    /// </summary>
    public bool IsCustom { get; set; }

    public virtual LinkEntity Link { get; set; } = null!;
}
