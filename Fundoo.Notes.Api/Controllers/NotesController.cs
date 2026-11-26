using System.Security.Claims;
using Fundoo.Notes.Api.Contracts;
using Fundoo.Notes.Application.Interfaces;
using Fundoo.Notes.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fundoo.Notes.Api.Controllers;

[ApiController]
[Route("api/v1/notes")]
[Authorize]
[ApiExplorerSettings(GroupName = "v1")]
public class NotesController : ControllerBase
{
    private readonly INoteRepository _notes;

    public NotesController(INoteRepository notes)
    {
        _notes = notes;
    }

    private Guid? CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirstValue("sub")
                    ?? User.FindFirstValue("userId");

        return Guid.TryParse(value, out var id) ? id : null;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateNoteRequest request,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var note = new Note
        {
            UserId = userId.Value,
            Title = (request.Title ?? "").Trim(),
            Content = request.Content ?? "",
            Color = request.Color,
            IsPinned = request.IsPinned
        };

        await _notes.CreateAsync(note, ct);

        return CreatedAtAction(
            nameof(GetById),
            new { id = note.Id },
            note);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Note>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        return Ok(await _notes.GetAllAsync(userId.Value, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var note = await _notes.GetByIdAsync(id, userId.Value, ct);

        return note == null ? NotFound() : Ok(note);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateNoteRequest request,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var access = await _notes.GetAccessAsync(id, userId.Value, ct);
        if (!access.Exists) return NotFound();
        if (!access.CanEdit) return Forbid();

        var note = new Note
        {
            Id = id,
            UserId = userId.Value,
            Title = (request.Title ?? "").Trim(),
            Content = request.Content ?? "",
            Color = request.Color,
            IsPinned = request.IsPinned
        };

        return await _notes.UpdateAsync(note, ct)
            ? NoContent()
            : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var access = await _notes.GetAccessAsync(id, userId.Value, ct);
        if (!access.Exists) return NotFound();
        if (!access.IsOwner) return Forbid();

        // Repository permanently deletes only if this note is in trash.
        return await _notes.DeleteAsync(id, userId.Value, ct)
            ? NoContent()
            : NotFound(new
            {
                message = "Note not found, or move it to trash first."
            });
    }

    [HttpGet("archived")]
    [ProducesResponseType(typeof(IEnumerable<Note>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Archived(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        return Ok(await _notes.GetArchivedAsync(userId.Value, ct));
    }

    [HttpGet("trash")]
    [ProducesResponseType(typeof(IEnumerable<Note>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Trash(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        return Ok(await _notes.GetTrashAsync(userId.Value, ct));
    }

    [HttpPut("{id:guid}/archive")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public Task<IActionResult> Archive(
        Guid id,
        CancellationToken ct)
        => ChangeArchive(id, true, ct);

    [HttpPut("{id:guid}/unarchive")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public Task<IActionResult> Unarchive(
        Guid id,
        CancellationToken ct)
        => ChangeArchive(id, false, ct);

    private async Task<IActionResult> ChangeArchive(
        Guid id,
        bool archived,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var access = await _notes.GetAccessAsync(id, userId.Value, ct);
        if (!access.Exists) return NotFound();
        if (!access.IsOwner) return Forbid();

        return await _notes.SetArchivedAsync(
            id, userId.Value, archived, ct)
            ? NoContent()
            : NotFound();
    }

    [HttpPut("{id:guid}/trash")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public Task<IActionResult> MoveToTrash(
        Guid id,
        CancellationToken ct)
        => ChangeTrash(id, true, ct);

    [HttpPut("{id:guid}/restore")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public Task<IActionResult> Restore(
        Guid id,
        CancellationToken ct)
        => ChangeTrash(id, false, ct);

    private async Task<IActionResult> ChangeTrash(
        Guid id,
        bool trashed,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var access = await _notes.GetAccessAsync(id, userId.Value, ct);
        if (!access.Exists) return NotFound();
        if (!access.IsOwner) return Forbid();

        return await _notes.SetTrashedAsync(
            id, userId.Value, trashed, ct)
            ? NoContent()
            : NotFound();
    }

    [HttpPut("{noteId:guid}/labels/{labelId:guid}")]
    public async Task<IActionResult> AddLabel(
        Guid noteId,
        Guid labelId,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var access = await _notes.GetAccessAsync(noteId, userId.Value, ct);
        if (!access.Exists) return NotFound();
        if (!access.IsOwner) return Forbid();

        return await _notes.AddLabelAsync(
            noteId, labelId, userId.Value, ct)
            ? NoContent()
            : NotFound(new
            {
                message = "Note or label not found for this user."
            });
    }

    [HttpDelete("{noteId:guid}/labels/{labelId:guid}")]
    public async Task<IActionResult> RemoveLabel(
        Guid noteId,
        Guid labelId,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var access = await _notes.GetAccessAsync(noteId, userId.Value, ct);
        if (!access.Exists) return NotFound();
        if (!access.IsOwner) return Forbid();

        return await _notes.RemoveLabelAsync(
            noteId, labelId, userId.Value, ct)
            ? NoContent()
            : NotFound();
    }

    [HttpGet("{id:guid}/collaborators")]
    [ProducesResponseType(typeof(IEnumerable<NoteCollaborator>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCollaborators(
        Guid id,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var access = await _notes.GetAccessAsync(id, userId.Value, ct);
        if (!access.Exists) return NotFound();
        if (!access.IsOwner) return Forbid();

        return Ok(await _notes.GetCollaboratorsAsync(id, ct));
    }

    [HttpPut("{id:guid}/collaborators")]
    [ProducesResponseType(typeof(NoteCollaborator), StatusCodes.Status200OK)]
    public async Task<IActionResult> ShareWithCollaborator(
        Guid id,
        [FromBody] ShareNoteRequest request,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        if (request.CollaboratorUserId == userId.Value)
        {
            return BadRequest(new { message = "Owner already has full access to this note." });
        }

        var access = await _notes.GetAccessAsync(id, userId.Value, ct);
        if (!access.Exists) return NotFound();
        if (!access.IsOwner) return Forbid();

        var permission = CollaboratorPermissions.Normalize(request.Permission);
        var collaborator = await _notes.UpsertCollaboratorAsync(
            id, userId.Value, request.CollaboratorUserId, permission, ct);

        return collaborator == null ? NotFound() : Ok(collaborator);
    }

    [HttpDelete("{id:guid}/collaborators/{collaboratorUserId:guid}")]
    public async Task<IActionResult> RemoveCollaborator(
        Guid id,
        Guid collaboratorUserId,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var access = await _notes.GetAccessAsync(id, userId.Value, ct);
        if (!access.Exists) return NotFound();
        if (!access.IsOwner) return Forbid();

        return await _notes.RemoveCollaboratorAsync(id, userId.Value, collaboratorUserId, ct)
            ? NoContent()
            : NotFound();
    }

    [HttpGet("{id:guid}/sharing-history")]
    [ProducesResponseType(typeof(IEnumerable<NoteSharingHistory>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSharingHistory(
        Guid id,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var access = await _notes.GetAccessAsync(id, userId.Value, ct);
        if (!access.Exists) return NotFound();
        if (!access.IsOwner) return Forbid();

        return Ok(await _notes.GetSharingHistoryAsync(id, ct));
    }
}