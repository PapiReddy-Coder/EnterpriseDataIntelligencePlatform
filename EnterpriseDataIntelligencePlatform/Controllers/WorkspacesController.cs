using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController]
[Route("api/workspaces")]
public sealed class WorkspacesController(
    AppDbContext db,
    IAuditService audit) : ControllerBase
{
    [HttpPost]
    [HasPermission(Permissions.WorkspacesManage)]
    public async Task<IActionResult> Create(
        CreateWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var code = request.Code?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new
            {
                message = "Workspace name is required."
            });
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new
            {
                message = "Workspace code is required."
            });
        }

        var duplicate = await db.Workspaces.AnyAsync(
            x => x.Name == name || x.Code == code,
            cancellationToken);

        if (duplicate)
        {
            return Conflict(new
            {
                message =
                    "A workspace with the same name or code already exists."
            });
        }

        var executionStrategy =
            db.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync<IActionResult>(
            async () =>
            {
                await using var transaction =
                    await db.Database.BeginTransactionAsync(
                        cancellationToken);

                try
                {
                    var workspace = new Workspace
                    {
                        Id = Guid.NewGuid(),
                        Name = name,
                        Code = code,
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = null
                    };

                    db.Workspaces.Add(workspace);

                    await db.SaveChangesAsync(cancellationToken);

                    await audit.WriteAsync(
                        action: "WorkspaceCreated",
                        entityType: "Workspace",
                        entityId: workspace.Id.ToString(),
                        details:
                            $"Workspace '{workspace.Name}' created.",
                        workspaceId: workspace.Id,
                        cancellationToken: cancellationToken);

                    await transaction.CommitAsync(
                        cancellationToken);

                    return CreatedAtAction(
                        nameof(Get),
                        new { id = workspace.Id },
                        workspace);
                }
                catch (DbUpdateException)
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    return Conflict(new
                    {
                        message =
                            "A workspace with the same name or code already exists."
                    });
                }
                catch
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    throw;
                }
            });
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.PlatformViewAll)]
    public async Task<IActionResult> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        var workspace = await db.Workspaces
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);

        if (workspace is null)
        {
            return NotFound(new
            {
                message = "Workspace was not found."
            });
        }

        return Ok(workspace);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.WorkspacesManage)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var workspace = await db.Workspaces
            .SingleOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);

        if (workspace is null)
        {
            return NotFound(new
            {
                message = "Workspace was not found."
            });
        }

        var name = request.Name?.Trim();
        var code = request.Code?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new
            {
                message = "Workspace name is required."
            });
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new
            {
                message = "Workspace code is required."
            });
        }

        var duplicate = await db.Workspaces.AnyAsync(
            x => x.Id != id &&
                 (x.Name == name || x.Code == code),
            cancellationToken);

        if (duplicate)
        {
            return Conflict(new
            {
                message =
                    "Another workspace already uses this name or code."
            });
        }

        var executionStrategy =
            db.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync<IActionResult>(
            async () =>
            {
                await using var transaction =
                    await db.Database.BeginTransactionAsync(
                        cancellationToken);

                try
                {
                    workspace.Name = name;
                    workspace.Code = code;
                    workspace.UpdatedAtUtc = DateTime.UtcNow;

                    await db.SaveChangesAsync(
                        cancellationToken);

                    await audit.WriteAsync(
                        action: "WorkspaceUpdated",
                        entityType: "Workspace",
                        entityId: workspace.Id.ToString(),
                        details:
                            $"Workspace '{workspace.Name}' updated.",
                        workspaceId: workspace.Id,
                        cancellationToken: cancellationToken);

                    await transaction.CommitAsync(
                        cancellationToken);

                    return Ok(workspace);
                }
                catch (DbUpdateException)
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    return Conflict(new
                    {
                        message =
                            "Another workspace already uses this name or code."
                    });
                }
                catch
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    throw;
                }
            });
    }

    [HttpPatch("{id:guid}/status")]
    [HasPermission(Permissions.WorkspacesManage)]
    public async Task<IActionResult> Status(
        Guid id,
        [FromQuery] bool active,
        CancellationToken cancellationToken)
    {
        var workspace = await db.Workspaces
            .SingleOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);

        if (workspace is null)
        {
            return NotFound(new
            {
                message = "Workspace was not found."
            });
        }

        if (workspace.IsActive == active)
        {
            return Conflict(new
            {
                message = active
                    ? "Workspace is already active."
                    : "Workspace is already inactive."
            });
        }

        var executionStrategy =
            db.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync<IActionResult>(
            async () =>
            {
                await using var transaction =
                    await db.Database.BeginTransactionAsync(
                        cancellationToken);

                try
                {
                    workspace.IsActive = active;
                    workspace.UpdatedAtUtc = DateTime.UtcNow;

                    await db.SaveChangesAsync(
                        cancellationToken);

                    await audit.WriteAsync(
                        action: active
                            ? "WorkspaceActivated"
                            : "WorkspaceDeactivated",
                        entityType: "Workspace",
                        entityId: workspace.Id.ToString(),
                        details: active
                            ? $"Workspace '{workspace.Name}' activated."
                            : $"Workspace '{workspace.Name}' deactivated.",
                        workspaceId: workspace.Id,
                        cancellationToken: cancellationToken);

                    await transaction.CommitAsync(
                        cancellationToken);

                    return NoContent();
                }
                catch
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    throw;
                }
            });
    }
}