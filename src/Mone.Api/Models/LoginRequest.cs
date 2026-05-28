using System.ComponentModel.DataAnnotations;

namespace Mone.Api.Models;

public sealed record LoginRequest(
    [Required] string Email,
    [Required] string Password);
