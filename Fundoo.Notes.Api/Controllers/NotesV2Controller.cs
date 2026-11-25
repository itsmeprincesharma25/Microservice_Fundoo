using System.Security.Claims;
using Fundoo.Notes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fundoo.Notes.Api.Controllers;

[ApiController]
[Route("api/v2/notes")]
[Authorize]
[ApiExplorerSettings(GroupName = "v2")]
[Tags("NotesV")]
public class NotesV2Controller : ControllerBase
{
    private readonly INoteRepository _notes;

    public NotesV2Controller(INoteRepository notes)
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

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var notes = await _notes.GetAllAsync(userId.Value, ct);
        return Ok(new
        {
            version = "v2",
            count = notes.Count,
            data = notes
        });
    }
}
