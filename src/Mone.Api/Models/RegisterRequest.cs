using System.ComponentModel.DataAnnotations;

namespace Mone.Api.Models;

public sealed record RegisterRequest(
    [Required] string Email,
    [Required, MinLength(8)] string Password);
