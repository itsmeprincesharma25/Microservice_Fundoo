using Fundoo.Notes.Application.Interfaces;
using Fundoo.Notes.Domain.Entities;
using Fundoo.Notes.Infrastructure.Data;
using Npgsql;
using NpgsqlTypes;

namespace Fundoo.Notes.Infrastructure.Repositories;

public class NoteRepository : INoteRepository
{
    private readonly NotesConnectionFactory _connectionFactory;

    public NoteRepository(NotesConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Note> CreateAsync(
        Note note,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO "Notes"
            (
                "Id", "UserId", "Title", "Content", "Color",
                "IsPinned", "IsArchived", "IsTrashed",
                "CreatedAtUtc", "UpdatedAtUtc"
            )
            VALUES
            (
                @Id, @UserId, @Title, @Content, @Color,
                @IsPinned, @IsArchived, @IsTrashed,
                @CreatedAtUtc, @UpdatedAtUtc
            );
            """;

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("Id", note.Id);
        command.Parameters.AddWithValue("UserId", note.UserId);
        command.Parameters.AddWithValue("Title", note.Title);
        command.Parameters.AddWithValue("Content", note.Content);

        command.Parameters.AddWithValue(
            "Color",
            (object?)note.Color ?? DBNull.Value);

        command.Parameters.AddWithValue("IsPinned", note.IsPinned);
        command.Parameters.AddWithValue("IsArchived", note.IsArchived);
        command.Parameters.AddWithValue("IsTrashed", note.IsTrashed);
        command.Parameters.AddWithValue("CreatedAtUtc", note.CreatedAtUtc);
        command.Parameters.AddWithValue("UpdatedAtUtc", note.UpdatedAtUtc);

        await command.ExecuteNonQueryAsync(ct);

        return note;
    }

    public async Task<Note?> GetByIdAsync(
        Guid id,
        Guid userId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT n.*
            FROM "Notes" n
            WHERE n."Id" = @Id
              AND (
                    n."UserId" = @UserId
                    OR (
                        n."IsTrashed" = FALSE
                        AND EXISTS (
                            SELECT 1
                            FROM "NoteCollaborators" nc
                            WHERE nc."NoteId" = n."Id"
                              AND nc."CollaboratorUserId" = @UserId
                        )
                    )
              );
            """;

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(ct);

        Note? note;

        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("Id", id);
            command.Parameters.AddWithValue("UserId", userId);

            await using var reader = await command.ExecuteReaderAsync(ct);

            note = await reader.ReadAsync(ct)
                ? ReadNote(reader)
                : null;
        }

        if (note != null)
        {
            await LoadLabelsAsync(connection, new[] { note }, ct);
        }

        return note;
    }

    public async Task<List<Note>> GetAllAsync(
        Guid userId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT DISTINCT n.*
            FROM "Notes" n
            LEFT JOIN "NoteCollaborators" nc
                ON nc."NoteId" = n."Id"
               AND nc."CollaboratorUserId" = @UserId
            WHERE (n."UserId" = @UserId OR nc."CollaboratorUserId" IS NOT NULL)
              AND n."IsArchived" = FALSE
              AND n."IsTrashed" = FALSE
            ORDER BY n."IsPinned" DESC, n."UpdatedAtUtc" DESC;
            """;

        return await GetNotesAsync(sql, userId, ct);
    }

    public async Task<List<Note>> GetArchivedAsync(
        Guid userId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT *
            FROM "Notes"
            WHERE "UserId" = @UserId
              AND "IsArchived" = TRUE
              AND "IsTrashed" = FALSE
            ORDER BY "UpdatedAtUtc" DESC;
            """;

        return await GetNotesAsync(sql, userId, ct);
    }

    public async Task<List<Note>> GetTrashAsync(
        Guid userId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT *
            FROM "Notes"
            WHERE "UserId" = @UserId
              AND "IsTrashed" = TRUE
            ORDER BY "UpdatedAtUtc" DESC;
            """;

        return await GetNotesAsync(sql, userId, ct);
    }

    private async Task<List<Note>> GetNotesAsync(
        string sql,
        Guid userId,
        CancellationToken ct)
    {
        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(ct);

        var notes = new List<Note>();

        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("UserId", userId);

            await using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                notes.Add(ReadNote(reader));
            }
        }

        if (notes.Count > 0)
        {
            await LoadLabelsAsync(connection, notes, ct);
        }

        return notes;
    }

    public async Task<bool> UpdateAsync(
        Note note,
        CancellationToken ct)
    {
        const string sql = """
            UPDATE "Notes" n
            SET "Title" = @Title,
                "Content" = @Content,
                "Color" = @Color,
                "IsPinned" = @IsPinned,
                "UpdatedAtUtc" = @UpdatedAtUtc
            WHERE n."Id" = @Id
              AND (
                    n."UserId" = @UserId
                    OR (
                        n."IsTrashed" = FALSE
                        AND EXISTS (
                            SELECT 1
                            FROM "NoteCollaborators" nc
                            WHERE nc."NoteId" = n."Id"
                              AND nc."CollaboratorUserId" = @UserId
                              AND LOWER(COALESCE(NULLIF(TRIM(nc."Permission"), ''), 'read')) = 'edit'
                        )
                    )
              );
            """;

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("Id", note.Id);
        command.Parameters.AddWithValue("UserId", note.UserId);
        command.Parameters.AddWithValue("Title", note.Title);
        command.Parameters.AddWithValue("Content", note.Content);

        command.Parameters.AddWithValue(
            "Color",
            (object?)note.Color ?? DBNull.Value);

        command.Parameters.AddWithValue("IsPinned", note.IsPinned);
        command.Parameters.AddWithValue(
            "UpdatedAtUtc", DateTime.UtcNow);

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        Guid userId,
        CancellationToken ct)
    {
        // Permanent deletion is allowed only after the note is trashed.
        const string sql = """
            DELETE FROM "Notes"
            WHERE "Id" = @Id
              AND "UserId" = @UserId
              AND "IsTrashed" = TRUE;
            """;

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("Id", id);
        command.Parameters.AddWithValue("UserId", userId);

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> SetArchivedAsync(
        Guid id,
        Guid userId,
        bool archived,
        CancellationToken ct)
    {
        const string sql = """
            UPDATE "Notes"
            SET "IsArchived" = @Archived,
                "IsPinned" = CASE
                    WHEN @Archived THEN FALSE
                    ELSE "IsPinned"
                END,
                "UpdatedAtUtc" = @UpdatedAtUtc
            WHERE "Id" = @Id
              AND "UserId" = @UserId
              AND "IsTrashed" = FALSE;
            """;

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("Id", id);
        command.Parameters.AddWithValue("UserId", userId);
        command.Parameters.AddWithValue("Archived", archived);
        command.Parameters.AddWithValue(
            "UpdatedAtUtc", DateTime.UtcNow);

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> SetTrashedAsync(
        Guid id,
        Guid userId,
        bool trashed,
        CancellationToken ct)
    {
        const string sql = """
            UPDATE "Notes"
            SET "IsTrashed" = @Trashed,
                "IsArchived" = CASE
                    WHEN @Trashed THEN FALSE
                    ELSE "IsArchived"
                END,
                "IsPinned" = CASE
                    WHEN @Trashed THEN FALSE
                    ELSE "IsPinned"
                END,
                "UpdatedAtUtc" = @UpdatedAtUtc
            WHERE "Id" = @Id
              AND "UserId" = @UserId;
            """;

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("Id", id);
        command.Parameters.AddWithValue("UserId", userId);
        command.Parameters.AddWithValue("Trashed", trashed);
        command.Parameters.AddWithValue(
            "UpdatedAtUtc", DateTime.UtcNow);

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> AddLabelAsync(
        Guid noteId,
        Guid labelId,
        Guid userId,
        CancellationToken ct)
    {
        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(ct);

        await using var transaction =
            await connection.BeginTransactionAsync(ct);

        const string validateSql = """
            SELECT EXISTS
            (
                SELECT 1
                FROM "Notes" n
                INNER JOIN "Labels" l
                    ON l."Id" = @LabelId
                WHERE n."Id" = @NoteId
                  AND n."UserId" = @UserId
                  AND l."UserId" = @UserId
                  AND n."IsTrashed" = FALSE
            );
            """;

        await using (var validateCommand =
            new NpgsqlCommand(validateSql, connection, transaction))
        {
            validateCommand.Parameters.AddWithValue("NoteId", noteId);
            validateCommand.Parameters.AddWithValue("LabelId", labelId);
            validateCommand.Parameters.AddWithValue("UserId", userId);

            var valid = (bool)(await validateCommand.ExecuteScalarAsync(ct)
                ?? false);

            if (!valid)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }
        }

        const string insertSql = """
            INSERT INTO "NoteLabels" ("NoteId", "LabelId")
            VALUES (@NoteId, @LabelId)
            ON CONFLICT ("NoteId", "LabelId") DO NOTHING;
            """;

        await using (var insertCommand =
            new NpgsqlCommand(insertSql, connection, transaction))
        {
            insertCommand.Parameters.AddWithValue("NoteId", noteId);
            insertCommand.Parameters.AddWithValue("LabelId", labelId);

            // Existing link is treated as success (idempotent operation).
            await insertCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> RemoveLabelAsync(
        Guid noteId,
        Guid labelId,
        Guid userId,
        CancellationToken ct)
    {
        const string sql = """
            DELETE FROM "NoteLabels" nl
            USING "Notes" n, "Labels" l
            WHERE nl."NoteId" = n."Id"
              AND nl."LabelId" = l."Id"
              AND nl."NoteId" = @NoteId
              AND nl."LabelId" = @LabelId
              AND n."UserId" = @UserId
              AND l."UserId" = @UserId;
            """;

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("NoteId", noteId);
        command.Parameters.AddWithValue("LabelId", labelId);
        command.Parameters.AddWithValue("UserId", userId);

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    private static Note ReadNote(NpgsqlDataReader reader)
    {
        return new Note
        {
            Id = reader.GetGuid(reader.GetOrdinal("Id")),
            UserId = reader.GetGuid(reader.GetOrdinal("UserId")),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Content = reader.GetString(reader.GetOrdinal("Content")),
            Color = reader.IsDBNull(reader.GetOrdinal("Color"))
                ? null
                : reader.GetString(reader.GetOrdinal("Color")),
            IsPinned = reader.GetBoolean(reader.GetOrdinal("IsPinned")),
            IsArchived = reader.GetBoolean(reader.GetOrdinal("IsArchived")),
            IsTrashed = reader.GetBoolean(reader.GetOrdinal("IsTrashed")),
            CreatedAtUtc = reader.GetDateTime(
                reader.GetOrdinal("CreatedAtUtc")),
            UpdatedAtUtc = reader.GetDateTime(
                reader.GetOrdinal("UpdatedAtUtc"))
        };
    }

    private static async Task LoadLabelsAsync(
        NpgsqlConnection connection,
        IReadOnlyCollection<Note> notes,
        CancellationToken ct)
    {
        var noteIds = notes.Select(n => n.Id).ToArray();

        const string sql = """
            SELECT
                nl."NoteId",
                l."Id" AS "LabelId",
                l."UserId" AS "LabelUserId",
                l."Name" AS "LabelName"
            FROM "NoteLabels" nl
            INNER JOIN "Labels" l
                ON l."Id" = nl."LabelId"
            WHERE nl."NoteId" = ANY(@NoteIds);
            """;

        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "NoteIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid, noteIds);

        var notesById = notes.ToDictionary(n => n.Id);

        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var noteId = reader.GetGuid(reader.GetOrdinal("NoteId"));

            if (!notesById.TryGetValue(noteId, out var note))
            {
                continue;
            }

            note.NoteLabels.Add(new NoteLabel
            {
                NoteId = noteId,
                LabelId = reader.GetGuid(reader.GetOrdinal("LabelId")),
                Label = new Label
                {
                    Id = reader.GetGuid(reader.GetOrdinal("LabelId")),
                    UserId = reader.GetGuid(
                        reader.GetOrdinal("LabelUserId")),
                    Name = reader.GetString(
                        reader.GetOrdinal("LabelName"))
                }
            });
        }
    }

    public async Task<NoteAccessInfo> GetAccessAsync(
        Guid noteId,
        Guid userId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT
                n."UserId" = @UserId AS "IsOwner",
                n."IsTrashed",
                nc."Permission"
            FROM "Notes" n
            LEFT JOIN "NoteCollaborators" nc
                ON nc."NoteId" = n."Id"
               AND nc."CollaboratorUserId" = @UserId
            WHERE n."Id" = @NoteId;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("NoteId", noteId);
        command.Parameters.AddWithValue("UserId", userId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return new NoteAccessInfo(false, false, null);
        }

        var isOwner = reader.GetBoolean(reader.GetOrdinal("IsOwner"));
        var isTrashed = reader.GetBoolean(reader.GetOrdinal("IsTrashed"));
        var hasCollab = !reader.IsDBNull(reader.GetOrdinal("Permission"));

        if (isOwner)
        {
            return new NoteAccessInfo(true, true, "Owner");
        }

        if (!hasCollab || isTrashed)
        {
            return new NoteAccessInfo(false, false, null);
        }

        var rawPermission = reader.GetString(reader.GetOrdinal("Permission"));
        return new NoteAccessInfo(true, false, CollaboratorPermissions.Normalize(rawPermission));
    }

    public async Task<List<NoteCollaborator>> GetCollaboratorsAsync(
        Guid noteId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT "Id", "NoteId", "CollaboratorUserId", "Permission", "CreatedAtUtc", "UpdatedAtUtc"
            FROM "NoteCollaborators"
            WHERE "NoteId" = @NoteId
            ORDER BY "CreatedAtUtc" ASC;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("NoteId", noteId);

        var list = new List<NoteCollaborator>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new NoteCollaborator
            {
                Id = reader.GetGuid(reader.GetOrdinal("Id")),
                NoteId = reader.GetGuid(reader.GetOrdinal("NoteId")),
                CollaboratorUserId = reader.GetGuid(reader.GetOrdinal("CollaboratorUserId")),
                Permission = CollaboratorPermissions.Normalize(
                    reader.IsDBNull(reader.GetOrdinal("Permission"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("Permission"))),
                CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc")),
                UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("UpdatedAtUtc"))
            });
        }

        return list;
    }

    public async Task<NoteCollaborator?> UpsertCollaboratorAsync(
        Guid noteId,
        Guid ownerUserId,
        Guid collaboratorUserId,
        string permission,
        CancellationToken ct)
    {
        var normalized = CollaboratorPermissions.Normalize(permission);
        var now = DateTime.UtcNow;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        const string ownerSql = """
            SELECT COUNT(1)
            FROM "Notes"
            WHERE "Id" = @NoteId AND "UserId" = @OwnerUserId;
            """;
        await using (var ownerCmd = new NpgsqlCommand(ownerSql, connection, transaction))
        {
            ownerCmd.Parameters.AddWithValue("NoteId", noteId);
            ownerCmd.Parameters.AddWithValue("OwnerUserId", ownerUserId);
            var count = (long)(await ownerCmd.ExecuteScalarAsync(ct) ?? 0L);
            if (count == 0)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }
        }

        Guid? existingId = null;
        string? oldPermission = null;
        DateTime existingCreatedAt = now;

        const string existingSql = """
            SELECT "Id", "Permission", "CreatedAtUtc"
            FROM "NoteCollaborators"
            WHERE "NoteId" = @NoteId AND "CollaboratorUserId" = @CollaboratorUserId;
            """;
        await using (var existingCmd = new NpgsqlCommand(existingSql, connection, transaction))
        {
            existingCmd.Parameters.AddWithValue("NoteId", noteId);
            existingCmd.Parameters.AddWithValue("CollaboratorUserId", collaboratorUserId);
            await using var reader = await existingCmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                existingId = reader.GetGuid(reader.GetOrdinal("Id"));
                oldPermission = CollaboratorPermissions.Normalize(
                    reader.IsDBNull(reader.GetOrdinal("Permission"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("Permission")));
                existingCreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc"));
            }
        }

        var collab = new NoteCollaborator
        {
            Id = existingId ?? Guid.NewGuid(),
            NoteId = noteId,
            CollaboratorUserId = collaboratorUserId,
            Permission = normalized,
            CreatedAtUtc = existingCreatedAt,
            UpdatedAtUtc = now
        };

        if (existingId == null)
        {
            const string insertCollabSql = """
                INSERT INTO "NoteCollaborators"
                ("Id", "NoteId", "CollaboratorUserId", "Permission", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES (@Id, @NoteId, @CollaboratorUserId, @Permission, @CreatedAtUtc, @UpdatedAtUtc);
                """;
            await using var insertCmd = new NpgsqlCommand(insertCollabSql, connection, transaction);
            insertCmd.Parameters.AddWithValue("Id", collab.Id);
            insertCmd.Parameters.AddWithValue("NoteId", noteId);
            insertCmd.Parameters.AddWithValue("CollaboratorUserId", collaboratorUserId);
            insertCmd.Parameters.AddWithValue("Permission", normalized);
            insertCmd.Parameters.AddWithValue("CreatedAtUtc", now);
            insertCmd.Parameters.AddWithValue("UpdatedAtUtc", now);
            await insertCmd.ExecuteNonQueryAsync(ct);

            await InsertSharingHistoryAsync(
                connection, transaction, noteId, collaboratorUserId, ownerUserId,
                "Added", null, normalized, now, ct);
        }
        else if (!string.Equals(oldPermission, normalized, StringComparison.OrdinalIgnoreCase))
        {
            const string updateCollabSql = """
                UPDATE "NoteCollaborators"
                SET "Permission" = @Permission, "UpdatedAtUtc" = @UpdatedAtUtc
                WHERE "Id" = @Id;
                """;
            await using var updateCmd = new NpgsqlCommand(updateCollabSql, connection, transaction);
            updateCmd.Parameters.AddWithValue("Id", collab.Id);
            updateCmd.Parameters.AddWithValue("Permission", normalized);
            updateCmd.Parameters.AddWithValue("UpdatedAtUtc", now);
            await updateCmd.ExecuteNonQueryAsync(ct);

            await InsertSharingHistoryAsync(
                connection, transaction, noteId, collaboratorUserId, ownerUserId,
                "PermissionChanged", oldPermission, normalized, now, ct);
        }

        await transaction.CommitAsync(ct);
        return collab;
    }

    public async Task<bool> RemoveCollaboratorAsync(
        Guid noteId,
        Guid ownerUserId,
        Guid collaboratorUserId,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        const string findSql = """
            SELECT nc."Permission"
            FROM "NoteCollaborators" nc
            INNER JOIN "Notes" n ON n."Id" = nc."NoteId"
            WHERE nc."NoteId" = @NoteId
              AND nc."CollaboratorUserId" = @CollaboratorUserId
              AND n."UserId" = @OwnerUserId;
            """;

        string? oldPermission = null;
        await using (var findCmd = new NpgsqlCommand(findSql, connection, transaction))
        {
            findCmd.Parameters.AddWithValue("NoteId", noteId);
            findCmd.Parameters.AddWithValue("CollaboratorUserId", collaboratorUserId);
            findCmd.Parameters.AddWithValue("OwnerUserId", ownerUserId);
            var scalar = await findCmd.ExecuteScalarAsync(ct);
            if (scalar == null)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }
            oldPermission = CollaboratorPermissions.Normalize(scalar == DBNull.Value ? null : (string)scalar);
        }

        const string deleteSql = """
            DELETE FROM "NoteCollaborators"
            WHERE "NoteId" = @NoteId AND "CollaboratorUserId" = @CollaboratorUserId;
            """;
        await using (var deleteCmd = new NpgsqlCommand(deleteSql, connection, transaction))
        {
            deleteCmd.Parameters.AddWithValue("NoteId", noteId);
            deleteCmd.Parameters.AddWithValue("CollaboratorUserId", collaboratorUserId);
            await deleteCmd.ExecuteNonQueryAsync(ct);
        }

        await InsertSharingHistoryAsync(
            connection, transaction, noteId, collaboratorUserId, ownerUserId,
            "Removed", oldPermission, null, now, ct);

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<List<NoteSharingHistory>> GetSharingHistoryAsync(
        Guid noteId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT "Id", "NoteId", "CollaboratorUserId", "ChangedByUserId", "Action", "OldPermission", "NewPermission", "ChangedAtUtc"
            FROM "NoteSharingHistory"
            WHERE "NoteId" = @NoteId
            ORDER BY "ChangedAtUtc" ASC;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("NoteId", noteId);

        var history = new List<NoteSharingHistory>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            history.Add(new NoteSharingHistory
            {
                Id = reader.GetGuid(reader.GetOrdinal("Id")),
                NoteId = reader.GetGuid(reader.GetOrdinal("NoteId")),
                CollaboratorUserId = reader.GetGuid(reader.GetOrdinal("CollaboratorUserId")),
                ChangedByUserId = reader.GetGuid(reader.GetOrdinal("ChangedByUserId")),
                Action = reader.GetString(reader.GetOrdinal("Action")),
                OldPermission = reader.IsDBNull(reader.GetOrdinal("OldPermission"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("OldPermission")),
                NewPermission = reader.IsDBNull(reader.GetOrdinal("NewPermission"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("NewPermission")),
                ChangedAtUtc = reader.GetDateTime(reader.GetOrdinal("ChangedAtUtc"))
            });
        }

        return history;
    }

    private static async Task InsertSharingHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid noteId,
        Guid collaboratorUserId,
        Guid changedByUserId,
        string action,
        string? oldPermission,
        string? newPermission,
        DateTime changedAtUtc,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO "NoteSharingHistory"
            ("Id", "NoteId", "CollaboratorUserId", "ChangedByUserId", "Action", "OldPermission", "NewPermission", "ChangedAtUtc")
            VALUES (@Id, @NoteId, @CollaboratorUserId, @ChangedByUserId, @Action, @OldPermission, @NewPermission, @ChangedAtUtc);
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("Id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("NoteId", noteId);
        cmd.Parameters.AddWithValue("CollaboratorUserId", collaboratorUserId);
        cmd.Parameters.AddWithValue("ChangedByUserId", changedByUserId);
        cmd.Parameters.AddWithValue("Action", action);
        cmd.Parameters.AddWithValue("OldPermission", (object?)oldPermission ?? DBNull.Value);
        cmd.Parameters.AddWithValue("NewPermission", (object?)newPermission ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ChangedAtUtc", changedAtUtc);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}