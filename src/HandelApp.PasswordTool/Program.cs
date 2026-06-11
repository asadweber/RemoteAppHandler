using Microsoft.AspNetCore.Identity;

var hasher = new PasswordHasher<string>();

while (true)
{
    Console.WriteLine();
    Console.WriteLine("HandelApp Password Tool");
    Console.WriteLine("=======================");
    Console.WriteLine("1. Generate hash for a password");
    Console.WriteLine("2. Verify a password against a hash");
    Console.WriteLine("3. Exit");
    Console.Write("Choice: ");

    var choice = Console.ReadLine()?.Trim();

    if (choice == "1")
    {
        Console.Write("Username : ");
        var username = Console.ReadLine() ?? string.Empty;

        Console.Write("Password : ");
        var password = ReadPassword();

        var hash = hasher.HashPassword(username, password);

        Console.WriteLine();
        Console.WriteLine("Hash (paste into appsettings.json PasswordHash field):");
        Console.WriteLine(hash);
    }
    else if (choice == "2")
    {
        Console.Write("Username     : ");
        var username = Console.ReadLine() ?? string.Empty;

        Console.Write("Password     : ");
        var password = ReadPassword();

        Console.Write("Stored hash  : ");
        var storedHash = Console.ReadLine() ?? string.Empty;

        var result = hasher.VerifyHashedPassword(username, storedHash, password);

        Console.WriteLine();
        Console.WriteLine(result switch
        {
            PasswordVerificationResult.Success             => "MATCH — password is correct.",
            PasswordVerificationResult.SuccessRehashNeeded => "MATCH — password correct but hash should be regenerated (older format).",
            _                                              => "NO MATCH — password is incorrect."
        });
    }
    else if (choice == "3")
    {
        break;
    }
    else
    {
        Console.WriteLine("Invalid choice.");
    }
}

static string ReadPassword()
{
    var pwd = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (pwd.Length > 0) pwd.Remove(pwd.Length - 1, 1);
        }
        else
        {
            pwd.Append(key.KeyChar);
        }
    }
    Console.WriteLine();
    return pwd.ToString();
}
