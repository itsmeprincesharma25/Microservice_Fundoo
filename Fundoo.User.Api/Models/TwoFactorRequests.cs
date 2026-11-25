using System.ComponentModel.DataAnnotations;

namespace Fundoo.User.Api.Models;

public class VerifyTwoFactorRequest
{
    public Guid ChallengeId { get; set; }

    public string? Code { get; set; }
}

public class ToggleTwoFactorRequest
{
    public bool Enable { get; set; }
}

public class ExternalAuthRequest
{
    [Required]
    public string Provider { get; set; } = "Google";

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string FullName { get; set; } = string.Empty;

    public string? ProviderKey { get; set; }
}
