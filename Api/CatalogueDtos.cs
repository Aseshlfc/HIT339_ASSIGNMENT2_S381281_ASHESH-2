using System.Text.Json.Serialization;

namespace LibraryManagementSystem.Api;

public record BranchRef(string Code, string Name);

/// <summary>
/// One catalogue item as the public API shows it. The fields that only apply to
/// one kind of item (author, artist, toyType...) are left out of the JSON for the
/// other kinds. Internal database ids are not exposed: libraryCode identifies an item.
/// </summary>
public record ItemDto
{
    public required string LibraryCode { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Type { get; init; }
    public required string Category { get; init; }
    public required string Status { get; init; }
    public required bool IsAvailable { get; init; }
    public required BranchRef? Branch { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Author { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Artist { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Genre { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Year { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ToyType { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? MinimumAge { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Manufacturer { get; init; }
}

public record PagedItems(int Page, int PageSize, int TotalItems, int TotalPages, IReadOnlyList<ItemDto> Items);

public record CategoryCount(string Name, int ItemCount);
public record TypeCategories(string Type, int ItemCount, int AvailableCount, IReadOnlyList<CategoryCount> Categories);

public record BranchStatus(string Code, string Name, string Address, string? Phone, bool IsActive, int ItemCount, int AvailableCount);
public record StatusTotals(int Items, int Available, int Borrowed, int Damaged, int InTransit);

/// <summary>Operating status: is the system up, which branches are open, how much stock is on the shelf.</summary>
public record OperatingStatus(
    string Status,
    string ApiVersion,
    DateTime ServerTimeUtc,
    bool DatabaseReachable,
    StatusTotals Totals,
    IReadOnlyList<BranchStatus> Branches);
