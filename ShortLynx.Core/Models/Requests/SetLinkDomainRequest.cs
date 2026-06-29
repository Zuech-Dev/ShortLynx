namespace ShortLynx.Core.Models.Requests;

/// <summary>Pins a link to a verified custom domain, or unpins it when CustomDomainId is null.</summary>
public sealed record SetLinkDomainRequest(Guid? CustomDomainId);
