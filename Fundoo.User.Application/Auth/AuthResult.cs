namespace Fundoo.User.Application.Auth;

public record AuthResult(bool Succeeded, string? Error, string? AccessToken)
{
    // NEW: id of the user created by RegisterAsync (Guid.Empty for other results)
    public Guid UserId { get; init; }

    // NEW: optional userId parameter; existing calls like Success() and Success(token) still work
    public static AuthResult Success(string? accessToken = null, Guid userId = default)
        => new(true, null, accessToken) { UserId = userId };

    public static AuthResult Failure(string error) => new(false, error, null);
}