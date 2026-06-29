using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

public sealed record AddDomainRequest([Required] string Domain);
