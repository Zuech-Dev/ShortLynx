using System.Net;
using System.Net.Http.Json;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;

namespace ShortLynx.Tests.Api;

public class MeLinksQrTests : IClassFixture<ApiFactory>
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private readonly ApiFactory _factory;
    public MeLinksQrTests(ApiFactory factory) => _factory = factory;

    private static async Task<(HttpClient Client, Guid LinkId, string Code)> CreateLinkAsync(ApiFactory f)
    {
        var (client, _, _) = await f.CreateSessionClientAsync();
        var link = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();
        return (client, link!.Id, link.ShortCode);
    }

    [Fact]
    public async Task Qr_DefaultFormat_ReturnsPngDownload()
    {
        var (client, id, code) = await CreateLinkAsync(_factory);

        var resp = await client.GetAsync($"/me/links/{id}/qr");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("image/png", resp.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", resp.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal($"{code}.png", resp.Content.Headers.ContentDisposition?.FileNameStar
            ?? resp.Content.Headers.ContentDisposition?.FileName);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.Equal(PngSignature, bytes[..PngSignature.Length]);
    }

    [Fact]
    public async Task Qr_SvgFormat_ReturnsSvgDownload()
    {
        var (client, id, code) = await CreateLinkAsync(_factory);

        var resp = await client.GetAsync($"/me/links/{id}/qr?format=svg");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("image/svg+xml", resp.Content.Headers.ContentType?.MediaType);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("<svg", body);
    }

    [Fact]
    public async Task Qr_UnknownFormat_Returns400()
    {
        var (client, id, _) = await CreateLinkAsync(_factory);
        var resp = await client.GetAsync($"/me/links/{id}/qr?format=gif");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Qr_LinkOfAnotherAccount_Returns404()
    {
        var (_, idA, _) = await CreateLinkAsync(_factory);
        var (clientB, _, _) = await _factory.CreateSessionClientAsync();

        var resp = await clientB.GetAsync($"/me/links/{idA}/qr");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Qr_WithoutSession_Returns401()
    {
        var (_, id, _) = await CreateLinkAsync(_factory);
        var resp = await _factory.CreateClient().GetAsync($"/me/links/{id}/qr");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
