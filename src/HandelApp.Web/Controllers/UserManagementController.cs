using HandelApp.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HandelApp.Web.Controllers;

[Authorize(Roles = "Admin")]
public class UserManagementController(
    IConfiguration configuration,
    IWebHostEnvironment env) : Controller
{
    private static readonly PasswordHasher<string> _hasher = new();

    // ── List ─────────────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Index()
    {
        var vm = new UserListViewModel
        {
            Users         = GetUsers()
                                .Select(u => new UserListViewModel.UserRow(u.Username, u.Role, u.IsActive))
                                .ToList(),
            ResultMessage = TempData["Result"] as string,
            IsError       = TempData["IsError"] is true
        };
        return View(vm);
    }

    // ── Add ──────────────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Add() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Add(string username, string password, string confirmPassword, string role)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            ModelState.AddModelError(string.Empty, "Username is required.");
            return View();
        }

        if (password != confirmPassword)
        {
            ModelState.AddModelError(string.Empty, "Passwords do not match.");
            return View();
        }

        if (password.Length < 6)
        {
            ModelState.AddModelError(string.Empty, "Password must be at least 6 characters.");
            return View();
        }

        var users = GetUsers();
        if (users.Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(string.Empty, $"Username '{username}' already exists.");
            return View();
        }

        var newUser = new UserEntry(
            username.Trim(),
            _hasher.HashPassword(username.Trim(), password),
            role,
            true);

        SaveUsers([.. users, newUser]);

        TempData["Result"] = $"User '{username}' created.";
        return RedirectToAction(nameof(Index));
    }

    // ── Deactivate / Activate ─────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetActive(string username, bool active)
    {
        if (string.Equals(username, User.Identity!.Name, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Result"]  = "Cannot deactivate your own account.";
            TempData["IsError"] = true;
            return RedirectToAction(nameof(Index));
        }

        MutateUser(username, u => u with { IsActive = active });
        TempData["Result"] = active
            ? $"User '{username}' activated."
            : $"User '{username}' deactivated.";
        return RedirectToAction(nameof(Index));
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(string username)
    {
        if (string.Equals(username, User.Identity!.Name, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Result"]  = "Cannot delete your own account.";
            TempData["IsError"] = true;
            return RedirectToAction(nameof(Index));
        }

        SaveUsers(GetUsers()
            .Where(u => !string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase))
            .ToList());

        TempData["Result"] = $"User '{username}' permanently deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private List<UserEntry> GetUsers()
        => configuration.GetSection("Auth:Users").Get<List<UserEntry>>() ?? [];

    private void MutateUser(string username, Func<UserEntry, UserEntry> mutate)
        => SaveUsers(GetUsers()
            .Select(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)
                ? mutate(u) : u)
            .ToList());

    private void SaveUsers(List<UserEntry> users)
    {
        var path = Path.Combine(env.ContentRootPath, "appsettings.json");
        var json = JsonNode.Parse(System.IO.File.ReadAllText(path))!;

        json["Auth"]!["Users"] = JsonNode.Parse(
            JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true }));

        System.IO.File.WriteAllText(path,
            json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        ((IConfigurationRoot)configuration).Reload();
    }

    internal sealed record UserEntry(
        string Username,
        string PasswordHash,
        string Role,
        bool IsActive = true);
}
