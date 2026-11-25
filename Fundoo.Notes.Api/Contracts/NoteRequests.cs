using System.ComponentModel.DataAnnotations;

namespace Fundoo.Notes.Api.Contracts;

public class CreateNoteRequest
{
    public string? Title { get; set; }

    public string? Content { get; set; }

    public string? Color { get; set; }

    public bool IsPinned { get; set; }
}

public class UpdateNoteRequest
{
    public string? Title { get; set; }

    public string? Content { get; set; }

    public string? Color { get; set; }

    public bool IsPinned { get; set; }
}

public class CreateLabelRequest
{
    public string? Name { get; set; }
}

public class RenameLabelRequest
{
    public string? Name { get; set; }
}
