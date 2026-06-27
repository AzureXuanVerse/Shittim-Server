using Microsoft.EntityFrameworkCore;
using Schale.Data.GameModel;
using Shittim.Commands;
using Serilog;

namespace Shittim.Services.Client
{
    public class ConsoleCommand
    {
        public static async Task ConsoleCommandListener(ConsoleClientConnection connection)
        {
            Log.Information("Starting console command with UID: {uid}", connection.AccountServerId);
            await InitializeTargetAccount(connection);
            await connection.SendChatMessage("Console GM ready. Use `uid <accountId>` to switch account, `accounts` to list accounts, `whoami` to show current account.");

            while (true)
            {
                var input = Console.ReadLine();
                if (input == null)
                {
                    await Task.Delay(100);
                    continue;
                }

                input = input.Trim();
                if (string.IsNullOrEmpty(input) || connection == null)
                    continue;

                if (await TryHandleLocalCommand(connection, input))
                    continue;

                var normalizedInput = input.StartsWith('/') ? input : "/" + input;
                if (normalizedInput.StartsWith('/'))
                {
                    try
                    {
                        var args = normalizedInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var commandName = args[0].TrimStart('/').ToLower();
                        var commandArgs = args.Skip(1).ToArray();

                        if (!CommandFactory.commands.ContainsKey(commandName))
                        {
                            await connection.SendChatMessage($"Unknown command: {commandName}");
                            continue;
                        }

                        if (!await EnsureTargetAccountSelected(connection))
                            continue;

                        var command = CommandFactory.CreateCommand(commandName, connection, commandArgs);
                        if (command == null)
                        {
                            await connection.SendChatMessage($"Unknown command: {commandName}");
                            continue;
                        }

                        await command.Execute();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error executing command: {ex.Message}");
                    }
                }
            }
        }

        private static async Task<bool> TryHandleLocalCommand(ConsoleClientConnection connection, string input)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return true;

            var localCommand = parts[0].TrimStart('/').ToLowerInvariant();
            switch (localCommand)
            {
                case "uid":
                case "use":
                    if (parts.Length < 2 || !long.TryParse(parts[1], out var accountId))
                    {
                        await connection.SendChatMessage("Usage: uid <accountId>");
                        return true;
                    }

                    await SelectTargetAccount(connection, accountId);
                    return true;

                case "whoami":
                    if (await TryGetCurrentAccount(connection) is AccountDBServer account)
                        await connection.SendChatMessage($"Current account: UID {account.ServerId}, Nickname: {account.Nickname}, Level: {account.Level}");
                    else
                        await connection.SendChatMessage("No active account selected. Use `uid <accountId>` first.");
                    return true;

                case "accounts":
                    await ListAccounts(connection);
                    return true;
            }

            return false;
        }

        private static async Task InitializeTargetAccount(ConsoleClientConnection connection)
        {
            if (connection.AccountServerId > 0 && await TryGetCurrentAccount(connection) is not null)
            {
                await connection.SendChatMessage($"Console default account: UID {connection.AccountServerId}");
                return;
            }

            using var context = await connection.Context.CreateDbContextAsync();
            var firstAccount = await context.Accounts
                .OrderBy(x => x.ServerId)
                .FirstOrDefaultAsync();

            if (firstAccount == null)
            {
                connection.AccountServerId = 0;
                await connection.SendChatMessage("No account found yet. Create/login an account first, then use `uid <accountId>`.");
                return;
            }

            connection.AccountServerId = firstAccount.ServerId;
            await connection.SendChatMessage($"Console auto-selected account: UID {firstAccount.ServerId}, Nickname: {firstAccount.Nickname}, Level: {firstAccount.Level}");
        }

        private static async Task<bool> EnsureTargetAccountSelected(ConsoleClientConnection connection)
        {
            if (await TryGetCurrentAccount(connection) is not null)
                return true;

            await connection.SendChatMessage("No valid account selected. Use `accounts` to list accounts, then `uid <accountId>` to select one.");
            return false;
        }

        private static async Task SelectTargetAccount(ConsoleClientConnection connection, long accountId)
        {
            using var context = await connection.Context.CreateDbContextAsync();
            var account = await context.Accounts.FirstOrDefaultAsync(x => x.ServerId == accountId);
            if (account == null)
            {
                await connection.SendChatMessage($"Account {accountId} not found.");
                return;
            }

            connection.AccountServerId = account.ServerId;
            await connection.SendChatMessage($"Switched console target account to UID {account.ServerId}, Nickname: {account.Nickname}, Level: {account.Level}");
        }

        private static async Task<AccountDBServer?> TryGetCurrentAccount(ConsoleClientConnection connection)
        {
            if (connection.AccountServerId <= 0)
                return null;

            using var context = await connection.Context.CreateDbContextAsync();
            return await context.Accounts.FirstOrDefaultAsync(x => x.ServerId == connection.AccountServerId);
        }

        private static async Task ListAccounts(ConsoleClientConnection connection)
        {
            using var context = await connection.Context.CreateDbContextAsync();
            var accounts = await context.Accounts
                .OrderBy(x => x.ServerId)
                .Take(20)
                .Select(x => new { x.ServerId, x.Nickname, x.Level })
                .ToListAsync();

            if (accounts.Count == 0)
            {
                await connection.SendChatMessage("No accounts found.");
                return;
            }

            await connection.SendChatMessage("Accounts:");
            foreach (var account in accounts)
            {
                await connection.SendChatMessage($"  UID {account.ServerId} | Lv {account.Level} | {account.Nickname}");
            }
        }
    }
}
