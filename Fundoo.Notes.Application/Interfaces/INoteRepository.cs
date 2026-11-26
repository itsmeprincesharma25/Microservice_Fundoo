using Fundoo.Notes.Domain.Entities;

namespace Fundoo.Notes.Application.Interfaces;

public record NoteAccessInfo(bool Exists, bool IsOwner, string? Permission)
{
    public bool CanRead => IsOwner || Permission != null;
    public bool CanEdit => IsOwner || string.Equals(Permission, CollaboratorPermissions.Edit, StringComparison.OrdinalIgnoreCase);
}

public interface INoteRepository
{
    Task<Note> CreateAsync(Note note, CancellationToken ct);
    Task<Note?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct);
    Task<List<Note>> GetAllAsync(Guid userId, CancellationToken ct);
    Task<List<Note>> GetArchivedAsync(Guid userId, CancellationToken ct);
    Task<List<Note>> GetTrashAsync(Guid userId, CancellationToken ct);
    Task<bool> UpdateAsync(Note note, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct);
    Task<bool> SetArchivedAsync(Guid id, Guid userId, bool archived, CancellationToken ct);
    Task<bool> SetTrashedAsync(Guid id, Guid userId, bool trashed, CancellationToken ct);
    Task<bool> AddLabelAsync(Guid noteId, Guid labelId, Guid userId, CancellationToken ct);
    Task<bool> RemoveLabelAsync(Guid noteId, Guid labelId, Guid userId, CancellationToken ct);

    Task<NoteAccessInfo> GetAccessAsync(Guid noteId, Guid userId, CancellationToken ct);
    Task<List<NoteCollaborator>> GetCollaboratorsAsync(Guid noteId, CancellationToken ct);
    Task<NoteCollaborator?> UpsertCollaboratorAsync(Guid noteId, Guid ownerUserId, Guid collaboratorUserId, string permission, CancellationToken ct);
    Task<bool> RemoveCollaboratorAsync(Guid noteId, Guid ownerUserId, Guid collaboratorUserId, CancellationToken ct);
    Task<List<NoteSharingHistory>> GetSharingHistoryAsync(Guid noteId, CancellationToken ct);
}