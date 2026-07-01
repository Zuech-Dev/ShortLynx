using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Operations;

namespace ShortLynx.Repository;

public class PostgresDbOperations(ShortLynxDbContext db) : IDbOperations
{
    public async Task BulkInsertUserLinkCodesAsync(IEnumerable<UserLinkCodeEntity> codes, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await using var writer = await conn.BeginBinaryImportAsync(
            """COPY "UserLinkCodes" ("Id","LinkId","UserId","Code","CreatedAt","IsActive","IsOneTimeUse","IsUsed") FROM STDIN (FORMAT BINARY)""",
            ct);
        foreach (var c in codes)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(c.Id, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(c.LinkId, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(c.UserId, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(c.Code, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(c.CreatedAt, NpgsqlDbType.TimestampTz, ct);
            await writer.WriteAsync(c.IsActive, NpgsqlDbType.Boolean, ct);
            await writer.WriteAsync(c.IsOneTimeUse, NpgsqlDbType.Boolean, ct);
            await writer.WriteAsync(c.IsUsed, NpgsqlDbType.Boolean, ct);
        }
        await writer.CompleteAsync(ct);
    }

    public async Task BulkInsertVisitsAsync(IEnumerable<VisitEntity> visits, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await using var writer = await conn.BeginBinaryImportAsync(
            """COPY "Visits" ("Id","ShortCodeId","ClickedAt","HashedIp","Source","Device","Browser","Os","ReferrerHost","Country","Language","NavigationType") FROM STDIN (FORMAT BINARY)""",
            ct);
        foreach (var v in visits)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(v.Id, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(v.ShortCodeId, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(v.ClickedAt, NpgsqlDbType.TimestampTz, ct);
            await writer.WriteAsync(v.HashedIp, NpgsqlDbType.Text, ct);
            await writer.WriteAsync((int)v.Source, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync((int)v.Device, NpgsqlDbType.Integer, ct);
            await WriteNullableTextAsync(writer, v.Browser, ct);
            await WriteNullableTextAsync(writer, v.Os, ct);
            await WriteNullableTextAsync(writer, v.ReferrerHost, ct);
            await WriteNullableTextAsync(writer, v.Country, ct);
            await WriteNullableTextAsync(writer, v.Language, ct);
            await WriteNullableTextAsync(writer, v.NavigationType, ct);
        }
        await writer.CompleteAsync(ct);
    }

    public async Task BulkInsertUserVisitsAsync(IEnumerable<UserVisitEntity> visits, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await using var writer = await conn.BeginBinaryImportAsync(
            """COPY "UserVisits" ("Id","UserLinkCodeId","UserId","ClickedAt","HashedIp","Source","Device","Browser","Os","ReferrerHost","Country","Language","NavigationType") FROM STDIN (FORMAT BINARY)""",
            ct);
        foreach (var v in visits)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(v.Id, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(v.UserLinkCodeId, NpgsqlDbType.Uuid, ct);
            if (v.UserId is null) await writer.WriteNullAsync(ct);
            else await writer.WriteAsync(v.UserId.Value, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(v.ClickedAt, NpgsqlDbType.TimestampTz, ct);
            await writer.WriteAsync(v.HashedIp, NpgsqlDbType.Text, ct);
            await writer.WriteAsync((int)v.Source, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync((int)v.Device, NpgsqlDbType.Integer, ct);
            await WriteNullableTextAsync(writer, v.Browser, ct);
            await WriteNullableTextAsync(writer, v.Os, ct);
            await WriteNullableTextAsync(writer, v.ReferrerHost, ct);
            await WriteNullableTextAsync(writer, v.Country, ct);
            await WriteNullableTextAsync(writer, v.Language, ct);
            await WriteNullableTextAsync(writer, v.NavigationType, ct);
        }
        await writer.CompleteAsync(ct);
    }

    private static async Task WriteNullableTextAsync(NpgsqlBinaryImporter writer, string? value, CancellationToken ct)
    {
        if (value is null) await writer.WriteNullAsync(ct);
        else await writer.WriteAsync(value, NpgsqlDbType.Text, ct);
    }

    private NpgsqlConnection GetConnection()
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            conn.Open();
        return conn;
    }
}