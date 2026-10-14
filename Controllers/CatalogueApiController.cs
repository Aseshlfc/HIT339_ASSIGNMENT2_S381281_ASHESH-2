using LibraryManagementSystem.Api;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LibraryManagementSystem.Controllers;

/// <summary>
/// Public, read-only REST API for external applications. Every action is GET, so any
/// other method is answered with 405. Items that have been written off (Destroy) are
/// never listed, matching the public search page. See docs/SECTION2_API.md.
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[ServiceFilter(typeof(ApiKeyFilter))]
[EnableRateLimiting(PublicApiOptions.PolicyName)]
public class CatalogueApiController : ControllerBase
{
    public const string ApiVersion = "1.0";

    private readonly LibraryContext _db;
    private readonly PublicApiOptions _options;

    public CatalogueApiController(LibraryContext db, IOptions<PublicApiOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    /// <summary>Search the catalogue. All filters are optional and combine with AND.</summary>
    /// <param name="q">Text found in the name, description or library code.</param>
    /// <param name="type">Book, Music or Toy.</param>
    /// <param name="category">A genre (books, music) or toy type (toys), exactly as /categories lists it.</param>
    /// <param name="branch">Branch code, for example CEN.</param>
    /// <param name="status">Available, Borrowed, Damaged or InTransit.</param>
    /// <param name="availableOnly">True to show only items that can be borrowed now.</param>
    [HttpGet("items")]
    public async Task<ActionResult<PagedItems>> GetItems(
        string? q = null, string? type = null, string? category = null, string? branch = null,
        string? status = null, bool availableOnly = false, int page = 1, int pageSize = 0)
    {
        if (page < 1)
            return BadRequestProblem("page must be 1 or more.");

        if (pageSize == 0) pageSize = _options.DefaultPageSize;
        if (pageSize < 1 || pageSize > _options.MaxPageSize)
            return BadRequestProblem($"pageSize must be between 1 and {_options.MaxPageSize}.");

        var query = _db.Items.AsNoTracking().Where(i => i.Status != ItemStatus.Destroy);

        if (!string.IsNullOrWhiteSpace(type))
        {
            switch (type.Trim().ToLowerInvariant())
            {
                case "book": query = query.OfType<Book>(); break;
                case "music": query = query.OfType<Music>(); break;
                case "toy": query = query.OfType<Toy>(); break;
                default: return BadRequestProblem("type must be Book, Music or Toy.");
            }
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            // Names only: Enum.TryParse would also accept numbers such as "1" or "99".
            if (status.Trim().All(c => char.IsDigit(c) || c == '-' || c == '+') ||
                !Enum.TryParse<ItemStatus>(status.Trim(), ignoreCase: true, out var wanted) ||
                !Enum.IsDefined(wanted) || wanted == ItemStatus.Destroy)
                return BadRequestProblem("status must be Available, Borrowed, Damaged or InTransit.");
            query = query.Where(i => i.Status == wanted);
        }

        if (availableOnly)
            query = query.Where(i => i.Status == ItemStatus.Available);

        if (!string.IsNullOrWhiteSpace(branch))
        {
            var branchCode = branch.Trim().ToUpper();
            var branchId = await _db.Branches.Where(b => b.Code.ToUpper() == branchCode).Select(b => (int?)b.Id).FirstOrDefaultAsync();
            if (branchId == null)
                return NotFoundProblem($"There is no branch with code \"{branch.Trim()}\".");
            query = query.Where(i => i.BranchId == branchId);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(i =>
                i.Name.ToLower().Contains(term) ||
                i.Description.ToLower().Contains(term) ||
                i.LibraryCode.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var wantedCategory = category.Trim().ToLower();
            query = query.Where(i =>
                (i is Book && ((Book)i).Genre.ToLower() == wantedCategory) ||
                (i is Music && ((Music)i).Genre.ToLower() == wantedCategory) ||
                (i is Toy && ((Toy)i).ToyType.ToLower() == wantedCategory));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(i => i.Name).ThenBy(i => i.LibraryCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var branches = await BranchLookupAsync();
        return new PagedItems(page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize),
            items.Select(i => ToDto(i, branches)).ToList());
    }

    /// <summary>One item, by its library code (not case sensitive).</summary>
    [HttpGet("items/{libraryCode}")]
    public async Task<ActionResult<ItemDto>> GetItem(string libraryCode)
    {
        var code = libraryCode.Trim().ToUpper();
        var item = await _db.Items.AsNoTracking()
            .Where(i => i.Status != ItemStatus.Destroy && i.LibraryCode.ToUpper() == code)
            .FirstOrDefaultAsync();

        if (item == null)
            return NotFoundProblem($"There is no item with library code \"{libraryCode}\".");

        return ToDto(item, await BranchLookupAsync());
    }

    /// <summary>Item types with how many items each holds, split by genre or toy type.</summary>
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<TypeCategories>>> GetCategories()
    {
        // The catalogue of a single library is small enough to group in memory.
        var items = await _db.Items.AsNoTracking().Where(i => i.Status != ItemStatus.Destroy).ToListAsync();

        var result = new List<TypeCategories>();
        foreach (var type in new[] { "Book", "Music", "Toy" })
        {
            var ofType = items.Where(i => i.ItemType == type).ToList();
            var categories = ofType
                .GroupBy(CategoryOf, StringComparer.OrdinalIgnoreCase)
                .Select(g => new CategoryCount(g.Key, g.Count()))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result.Add(new TypeCategories(type, ofType.Count, ofType.Count(i => i.Status == ItemStatus.Available), categories));
        }
        return result;
    }

    /// <summary>Whether the system is running, which branches exist and how much stock they hold.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<OperatingStatus>> GetStatus()
    {
        bool reachable;
        try { reachable = await _db.Database.CanConnectAsync(); }
        catch { reachable = false; }

        if (!reachable)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return new OperatingStatus("degraded", ApiVersion, DateTime.UtcNow, false,
                new StatusTotals(0, 0, 0, 0, 0), []);
        }

        var stock = await _db.Items.AsNoTracking()
            .Where(i => i.Status != ItemStatus.Destroy)
            .Select(i => new { i.BranchId, i.Status })
            .ToListAsync();
        var branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync();

        return new OperatingStatus("operational", ApiVersion, DateTime.UtcNow, true,
            new StatusTotals(
                stock.Count,
                stock.Count(s => s.Status == ItemStatus.Available),
                stock.Count(s => s.Status == ItemStatus.Borrowed),
                stock.Count(s => s.Status == ItemStatus.Damaged),
                stock.Count(s => s.Status == ItemStatus.InTransit)),
            branches.Select(b => new BranchStatus(b.Code, b.Name, b.Address, b.Phone, b.IsActive,
                stock.Count(s => s.BranchId == b.Id),
                stock.Count(s => s.BranchId == b.Id && s.Status == ItemStatus.Available))).ToList());
    }

    private async Task<Dictionary<int, BranchRef>> BranchLookupAsync() =>
        await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => new BranchRef(b.Code, b.Name));

    private static string CategoryOf(Item item) => item switch
    {
        Book b => b.Genre,
        Music m => string.IsNullOrWhiteSpace(m.Genre) ? "Uncategorised" : m.Genre,
        Toy t => t.ToyType,
        _ => "Uncategorised"
    };

    private static ItemDto ToDto(Item item, Dictionary<int, BranchRef> branches)
    {
        branches.TryGetValue(item.BranchId, out var branch);
        var dto = new ItemDto
        {
            LibraryCode = item.LibraryCode,
            Name = item.Name,
            Description = item.Description,
            Type = item.ItemType,
            Category = CategoryOf(item),
            Status = item.Status.ToString(),
            IsAvailable = item.Status == ItemStatus.Available,
            Branch = branch
        };

        return item switch
        {
            Book b => dto with { Author = b.Author, Genre = b.Genre, Year = b.PublicationYear },
            Music m => dto with
            {
                Artist = m.Artist,
                Genre = string.IsNullOrWhiteSpace(m.Genre) ? null : m.Genre,
                Year = m.ReleaseYear
            },
            Toy t => dto with
            {
                ToyType = t.ToyType,
                MinimumAge = t.MinimumAge,
                Manufacturer = string.IsNullOrWhiteSpace(t.Manufacturer) ? null : t.Manufacturer
            },
            _ => dto
        };
    }

    private ObjectResult BadRequestProblem(string detail) => ProblemResult(StatusCodes.Status400BadRequest, "Bad request", detail);
    private ObjectResult NotFoundProblem(string detail) => ProblemResult(StatusCodes.Status404NotFound, "Not found", detail);

    private ObjectResult ProblemResult(int status, string title, string detail) =>
        new(new ProblemDetails { Status = status, Title = title, Detail = detail })
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
}
