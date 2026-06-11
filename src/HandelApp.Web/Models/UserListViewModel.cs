namespace HandelApp.Web.Models;

public class UserListViewModel
{
    public List<UserRow> Users { get; init; } = [];
    public string? ResultMessage { get; init; }
    public bool IsError { get; init; }

    public sealed record UserRow(string Username, string Role, bool IsActive);
}
