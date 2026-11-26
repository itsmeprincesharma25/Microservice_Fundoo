namespace Fundoo.Notes.Domain.Entities;

public static class CollaboratorPermissions
{
    public const string Read = "Read";
    public const string Edit = "Edit";

    public static string Normalize(string? permission)
    {
        if (string.Equals(permission?.Trim(), Edit, StringComparison.OrdinalIgnoreCase))
            return Edit;

        return Read;
    }

    public static bool IsValid(string? permission)
    {
        var trimmed = permission?.Trim();
        return string.Equals(trimmed, Read, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, Edit, StringComparison.OrdinalIgnoreCase);
    }
}

public class NoteCollaborator
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NoteId { get; set; }
    public Guid CollaboratorUserId { get; set; }
    public string Permission { get; set; } = CollaboratorPermissions.Read;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class NoteSharingHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NoteId { get; set; }
    public Guid CollaboratorUserId { get; set; }
    public Guid ChangedByUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? OldPermission { get; set; }
    public string? NewPermission { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
