namespace ShortLynx.Services.ShortCodes;

public interface IShortCodeGenerator
{
    string Generate(Guid linkId, Guid? userId = null, int attempt = 0);
}
