using System.ComponentModel.DataAnnotations;

namespace Fundoo.User.Api.Models;

public class RegisterRequest
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }
}
