using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController]
[Route("api/dataset-categories")]
public sealed class DatasetCategoriesController(AppDbContext db, IAuditService audit) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.DatasetsView)]
    public async Task<IActionResult> List([FromQuery] bool activeOnly = true, CancellationToken ct = default)
    {
        var query = db.DatasetCategories.AsNoTracking();
        if (activeOnly) query = query.Where(x => x.IsActive);
        var items = await query.OrderBy(x => x.Name)
            .Select(x => new DatasetCategoryResponse(x.Id, x.Name, x.Description, x.IsActive))
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost]
    [HasPermission(Permissions.DatasetCategoriesManage)]
    public async Task<IActionResult> Create(CreateDatasetCategoryRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        var normalized = name.ToUpperInvariant();
        if (await db.DatasetCategories.AnyAsync(x => x.NormalizedName == normalized, ct))
            return Conflict("Dataset category already exists.");

        var category = new DatasetCategory
        {
            Name = name,
            NormalizedName = normalized,
            Description = request.Description?.Trim() ?? string.Empty
        };
        db.DatasetCategories.Add(category);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("DatasetCategoryCreated", "DatasetCategory", category.Id.ToString(),
            category.Name, cancellationToken: ct);
        return CreatedAtAction(nameof(List), new { id = category.Id },
            new DatasetCategoryResponse(category.Id, category.Name, category.Description, category.IsActive));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.DatasetCategoriesManage)]
    public async Task<IActionResult> Update(Guid id, UpdateDatasetCategoryRequest request, CancellationToken ct)
    {
        var category = await db.DatasetCategories.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (category is null) return NotFound();
        var normalized = request.Name.Trim().ToUpperInvariant();
        if (await db.DatasetCategories.AnyAsync(x => x.Id != id && x.NormalizedName == normalized, ct))
            return Conflict("Dataset category already exists.");

        category.Name = request.Name.Trim();
        category.NormalizedName = normalized;
        category.Description = request.Description?.Trim() ?? string.Empty;
        category.IsActive = request.IsActive;
        category.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("DatasetCategoryUpdated", "DatasetCategory", id.ToString(),
            category.Name, cancellationToken: ct);
        return NoContent();
    }
}
