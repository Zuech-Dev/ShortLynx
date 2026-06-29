namespace ShortLynx.Data.Enums;

/// <summary>
/// A user's role within an account, ordered by power so <c>role &gt;= Admin</c> comparisons work.
/// Persisted as an int.
/// </summary>
public enum AccountRole
{
    Viewer = 0,
    Member = 1,
    Admin = 2,
    Owner = 3,
}
