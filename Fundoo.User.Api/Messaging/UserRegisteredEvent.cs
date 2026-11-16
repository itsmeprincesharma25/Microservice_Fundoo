namespace Fundoo.User.Api.Messaging;

public record UserRegisteredEvent(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    DateTime OccurredAtUtc);