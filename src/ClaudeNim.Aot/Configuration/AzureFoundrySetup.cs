// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ClaudeNim.Aot.Compat;

namespace ClaudeNim.Aot.Configuration;

/// <summary>Discovers Azure chat deployments and saves a local proxy profile.</summary>
internal static class AzureFoundrySetup
{
    /// <summary>The NVIDIA backup chain saved for every Claude tier.</summary>
    private const string NvidiaFallbacks = "nvidia/nemotron-3-super-120b-a12b,deepseek-ai/deepseek-v4.1-flash";

    /// <summary>The profile's Azure options section.</summary>
    private const string AzureSection = "AzureFoundry";

    /// <summary>The preferred largest deployed model.</summary>
    private const string LargestModel = "gpt-6.1-sol";

    /// <summary>The Azure CLI service command.</summary>
    private const string CognitiveServices = "cognitiveservices";

    /// <summary>The Azure CLI resource command.</summary>
    private const string AccountCommand = "account";

    /// <summary>The Azure CLI JSON output flag.</summary>
    private const string OutputFlag = "--output";

    /// <summary>The Azure CLI quiet output flag.</summary>
    private const string OnlyErrorsFlag = "--only-show-errors";

    /// <summary>The characters that permit Windows shell expansion or command chaining.</summary>
    private static readonly SearchValues<char> ShellCharacters = SearchValues.Create("%!\"&|^<>\r\n");

    /// <summary>Resolves the same profile path for discovery and proxy startup.</summary>
    /// <param name="configured">An optional profile path.</param>
    /// <returns>The absolute profile path.</returns>
    internal static string PathFor(string? configured) => string.IsNullOrWhiteSpace(configured)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "claudenim", "azure.json")
        : Path.GetFullPath(configured);

    /// <summary>Runs setup and reports failures without printing Azure CLI response content.</summary>
    /// <param name="args">An optional resource group and account name.</param>
    /// <param name="path">The profile destination.</param>
    /// <param name="output">Receives setup status without credentials.</param>
    /// <param name="errors">Receives safe failure messages.</param>
    /// <param name="execute">Runs Azure CLI and returns its JSON output.</param>
    /// <param name="cancellationToken">Stops discovery and saving.</param>
    /// <returns>Zero after saving the profile; otherwise, one.</returns>
    internal static async Task<int> RunAsync(
        string[] args,
        string path,
        TextWriter output,
        TextWriter errors,
        Func<string[], CancellationToken, Task<string>> execute,
        CancellationToken cancellationToken)
    {
        string message;
        try
        {
            await ConfigureAsync(args, path, output, execute, cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (InvalidOperationException error)
        {
            message = $"Azure setup failed: {error.Message}";
        }
        catch (JsonException)
        {
            message = "Azure setup failed: Azure CLI returned invalid JSON. Check az login and the active subscription.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            message = "Azure setup failed: Could not read Azure configuration or save the profile. Check Azure CLI and profile access permissions.";
        }
        catch (OperationCanceledException)
        {
            message = "Azure setup timed out.";
        }

        await errors.WriteLineAsync(message.AsMemory(), CancellationToken.None).ConfigureAwait(false);
        return 1;
    }

    /// <summary>Configures a profile without changing Azure resources.</summary>
    /// <param name="args">An optional resource group and account name.</param>
    /// <param name="path">The profile destination.</param>
    /// <param name="output">Receives setup status without credentials.</param>
    /// <param name="execute">Runs the Azure CLI and returns its JSON output.</param>
    /// <param name="cancellationToken">Stops discovery and saving.</param>
    /// <returns>A task that completes after saving the profile.</returns>
    /// <exception cref="InvalidOperationException">The selected account or its deployments cannot be configured.</exception>
    internal static async Task ConfigureAsync(
        string[] args,
        string path,
        TextWriter output,
        Func<string[], CancellationToken, Task<string>> execute,
        CancellationToken cancellationToken)
    {
        const int selectedAccountArguments = 2;
        if (args.Length is not (0 or selectedAccountArguments))
        {
            throw new InvalidOperationException("Usage: --azure-configure [<resource-group> <account>]");
        }

        foreach (var name in args)
        {
            ValidateResourceName(name);
        }

        var accountsJson = await execute(
            [
                CognitiveServices, AccountCommand, "list", "--query",
                "[].{name:name,resourceGroup:resourceGroup,kind:kind,endpoint:properties.endpoint}",
                OutputFlag, "json", OnlyErrorsFlag,
            ],
            cancellationToken).ConfigureAwait(false);
        using var accounts = ParseJson(accountsJson, cancellationToken);
        var account = SelectAccount(accounts.RootElement, args, cancellationToken);
        var group = RequiredString(account, "resourceGroup");
        var accountName = RequiredString(account, "name");
        ValidateResourceName(group);
        ValidateResourceName(accountName);
        var endpoint = RequiredString(account, "endpoint");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps
            || address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0)
        {
            throw new InvalidOperationException("The Azure account did not return a valid HTTPS resource endpoint.");
        }

        var deploymentsJson = await execute(
            [
                CognitiveServices, AccountCommand, "deployment", "list", "--resource-group", group, "--name", accountName, "--query",
                "[].{name:name,model:properties.model,capabilities:properties.capabilities,provisioningState:properties.provisioningState}",
                OutputFlag, "json", OnlyErrorsFlag,
            ],
            cancellationToken).ConfigureAwait(false);
        using var deployments = ParseJson(deploymentsJson, cancellationToken);
        var models = ChatModels(deployments.RootElement, cancellationToken);
        if (models.Count == 0)
        {
            throw new InvalidOperationException("The selected Azure account has no ready chat-completion deployments.");
        }

        var keysJson = await execute(
            [CognitiveServices, AccountCommand, "keys", "list", "--resource-group", group, "--name", accountName, OutputFlag, "json", OnlyErrorsFlag],
            cancellationToken).ConfigureAwait(false);
        using var keys = ParseJson(keysJson, cancellationToken);
        var key = RequiredString(keys.RootElement, "key1");
        await SaveAsync(path, endpoint, key, models, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"Azure endpoint: {endpoint}".AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"Chat deployments: {string.Join(", ", models.Keys)}".AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"Saved profile: {path}".AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync("Set NVIDIA_API_KEY to enable the configured NVIDIA backup models.".AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs the installed Azure CLI without exposing its credential output.</summary>
    /// <param name="args">The Azure CLI arguments.</param>
    /// <param name="cancellationToken">Stops the CLI process and output reads.</param>
    /// <returns>The command's JSON output.</returns>
    /// <exception cref="InvalidOperationException">Azure CLI cannot be started or cannot read the selected resource.</exception>
    internal static async Task<string> ExecuteAsync(string[] args, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = CommandFor(FindAzureCli(), args, OperatingSystem.IsWindows()) };
        try
        {
            _ = process.Start();
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException("Could not start Azure CLI. Install az and ensure it is available on PATH.");
        }

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            var succeeded = await ProcessExitStatusCompat.SucceededAsync(process, cancellationToken).ConfigureAwait(false);
            var json = await output.ConfigureAwait(false);
            _ = await errors.ConfigureAwait(false);
            if (!succeeded)
            {
                // Azure CLI output can contain credentials, so report the operation without echoing it.
                var operation = args.Contains("keys", StringComparer.Ordinal) ? "resource keys" : "accounts or deployments";
                throw new InvalidOperationException($"Azure CLI could not read {operation}. Check az login, the active subscription, and account permissions.");
            }

            return json;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }

    /// <summary>Builds a direct invocation or a quoted Windows Azure CLI shim invocation.</summary>
    /// <param name="executable">The installed Azure CLI path.</param>
    /// <param name="args">The Azure CLI arguments.</param>
    /// <param name="windows">Whether the command runs on Windows.</param>
    /// <returns>The redirected process startup settings.</returns>
    internal static ProcessStartInfo CommandFor(string executable, string[] args, bool windows)
    {
        var command = new ProcessStartInfo { FileName = executable, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (windows && (executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            ValidateShellArgument(executable);
            foreach (var argument in args)
            {
                ValidateShellArgument(argument);
            }

            // call preserves the installed wrapper's path quoting without a composed shell command.
            command.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            command.ArgumentList.Add("/d");
            command.ArgumentList.Add("/v:off");
            command.ArgumentList.Add("/c");
            command.ArgumentList.Add("call");
            command.ArgumentList.Add(executable);
        }

        foreach (var argument in args)
        {
            command.ArgumentList.Add(argument);
        }

        return command;
    }

    /// <summary>Finds the Azure CLI installed on the process search path.</summary>
    /// <returns>The absolute executable or Windows shim path.</returns>
    /// <exception cref="InvalidOperationException">Azure CLI is not installed on the search path.</exception>
    private static string FindAzureCli()
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat" } : [string.Empty];
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory.Trim('"'), $"az{extension}");
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        throw new InvalidOperationException("Azure CLI was not found. Install az and ensure it is available on PATH.");
    }

    /// <summary>Checks user-selected and discovered names before invoking Azure CLI.</summary>
    /// <param name="name">The resource group or account name.</param>
    /// <exception cref="InvalidOperationException">The name is empty or contains unsupported characters.</exception>
    private static void ValidateResourceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Azure resource group and account names cannot be empty.");
        }

        foreach (var character in name)
        {
            if (!char.IsLetterOrDigit(character) && character is not ('_' or '-' or '.' or '(' or ')'))
            {
                throw new InvalidOperationException("Azure resource group and account names contain unsupported characters.");
            }
        }
    }

    /// <summary>Rejects expansion and quotation characters in Windows shim arguments.</summary>
    /// <param name="argument">A path or argument enclosed in double quotes.</param>
    /// <exception cref="InvalidOperationException">The argument permits shell expansion or command chaining.</exception>
    private static void ValidateShellArgument(string argument)
    {
        if (argument.AsSpan().IndexOfAny(ShellCharacters) >= 0)
        {
            throw new InvalidOperationException("The Azure CLI path or arguments cannot be safely passed to the Windows CLI wrapper.");
        }
    }

    /// <summary>Reads CLI JSON with cancellation checked before parsing.</summary>
    /// <param name="json">The Azure CLI response.</param>
    /// <param name="cancellationToken">Stops parsing before it starts.</param>
    /// <returns>The parsed JSON document.</returns>
    private static JsonDocument ParseJson(string json, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return JsonDocument.Parse(json);
    }

    /// <summary>Chooses one AI account, requiring names when discovery is ambiguous.</summary>
    /// <param name="accounts">The discovered accounts.</param>
    /// <param name="args">An optional resource group and account.</param>
    /// <param name="cancellationToken">Stops account selection.</param>
    /// <returns>The selected account in its owning JSON document.</returns>
    /// <exception cref="InvalidOperationException">Account discovery is empty or ambiguous.</exception>
    private static JsonElement SelectAccount(JsonElement accounts, string[] args, CancellationToken cancellationToken)
    {
        if (accounts.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Azure CLI did not return an account list.");
        }

        JsonElement? selected = null;
        foreach (var account in accounts.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = RequiredString(account, "kind");
            if (kind is not ("OpenAI" or "AIServices"))
            {
                continue;
            }

            if (args.Length != 0
                && (!string.Equals(RequiredString(account, "resourceGroup"), args[0], StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(RequiredString(account, "name"), args[1], StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (selected is not null)
            {
                throw new InvalidOperationException("More than one Azure AI account was found. Run --azure-configure <resource-group> <account>.");
            }

            selected = account;
        }

        return selected ?? throw new InvalidOperationException("No matching Azure AI account was found. Check az login and the active subscription.");
    }

    /// <summary>Retains ready deployments advertising chat completion capability.</summary>
    /// <param name="deployments">The discovered Azure deployments.</param>
    /// <param name="cancellationToken">Stops deployment selection.</param>
    /// <returns>Underlying model names keyed by deployment name.</returns>
    /// <exception cref="InvalidOperationException">Azure CLI returned an invalid deployment list.</exception>
    private static Dictionary<string, string> ChatModels(JsonElement deployments, CancellationToken cancellationToken)
    {
        if (deployments.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Azure CLI did not return a deployment list.");
        }

        var models = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var deployment in deployments.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deployment.TryGetProperty("provisioningState", out var state)
                && state.ValueKind == JsonValueKind.String && state.GetString() != "Succeeded")
            {
                continue;
            }

            if (!CanChat(deployment))
            {
                continue;
            }

            var name = RequiredString(deployment, "name");
            ValidateResourceName(name);
            var model = RequiredString(deployment.GetProperty("model"), "name");
            models[name] = model;
        }

        return models;
    }

    /// <summary>Checks the service's explicit chat-completion capability.</summary>
    /// <param name="deployment">The discovered deployment.</param>
    /// <returns>Whether the deployment supports chat completion requests.</returns>
    private static bool CanChat(JsonElement deployment)
    {
        if (!deployment.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Object
            || !capabilities.TryGetProperty("chatCompletion", out var chat))
        {
            return false;
        }

        return chat.ValueKind == JsonValueKind.True
            || (chat.ValueKind == JsonValueKind.String && string.Equals(chat.GetString(), "true", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Checks required CLI fields without including response content in errors.</summary>
    /// <param name="element">The object containing the field.</param>
    /// <param name="field">The required string field.</param>
    /// <returns>The nonempty field value.</returns>
    /// <exception cref="InvalidOperationException">Azure CLI omitted a required string field.</exception>
    private static string RequiredString(JsonElement element, string field)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(field, out var property)
            && property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString()))
        {
            return property.GetString()!;
        }

        throw new InvalidOperationException($"Azure CLI did not return the required {field} field.");
    }

    /// <summary>Saves the resource credential and routes atomically with owner-only Unix access.</summary>
    /// <param name="path">The absolute profile path.</param>
    /// <param name="endpoint">The Azure resource endpoint.</param>
    /// <param name="key">The resource credential.</param>
    /// <param name="models">Underlying model names keyed by deployment.</param>
    /// <param name="cancellationToken">Stops profile writing.</param>
    /// <returns>A task that completes after the profile is replaced.</returns>
    private static async Task SaveAsync(string path, string endpoint, string key, Dictionary<string, string> models, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var file = new FileStream(temporary, options))
            {
                await using var writer = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = true });
                writer.WriteStartObject();
                writer.WriteStartObject(AzureSection);
                writer.WriteBoolean("Enabled", true);
                writer.WriteString("BaseUrl", endpoint);
                writer.WriteString("ApiKey", key);
                writer.WriteStartObject("ModelNames");
                foreach (var (deployment, model) in models)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    writer.WriteString(deployment, model);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteStartObject("ModelCatalog");
                var qualified = new List<string>(models.Count);
                foreach (var deployment in models.Keys)
                {
                    qualified.Add($"azure:{deployment}");
                }

                writer.WriteString("AdditionalModels", string.Join(',', qualified));
                writer.WriteEndObject();
                writer.WriteStartObject("ModelRouting");
                writer.WriteBoolean("FallbackForExplicitModels", true);
                writer.WriteString("Default", Preferred(models, LargestModel));
                writer.WriteString("Opus", Preferred(models, LargestModel));
                writer.WriteString("Sonnet", Preferred(models, "gpt-6-astra"));
                writer.WriteString("Haiku", Preferred(models, "gpt-6-luna"));
                writer.WriteString("DefaultFallbacks", NvidiaFallbacks);
                writer.WriteString("OpusFallbacks", NvidiaFallbacks);
                writer.WriteString("SonnetFallbacks", NvidiaFallbacks);
                writer.WriteString("HaikuFallbacks", NvidiaFallbacks);
                writer.WriteEndObject();
                writer.WriteEndObject();
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Uses a preferred deployed model or the first available chat deployment.</summary>
    /// <param name="models">The deployed model names.</param>
    /// <param name="preferred">The underlying model preferred for this tier.</param>
    /// <returns>The qualified Azure deployment identifier.</returns>
    private static string Preferred(Dictionary<string, string> models, string preferred)
    {
        foreach (var (deployment, model) in models)
        {
            if (string.Equals(model, preferred, StringComparison.OrdinalIgnoreCase))
            {
                return $"azure:{deployment}";
            }
        }

        using var names = models.Keys.GetEnumerator();
        _ = names.MoveNext();
        return $"azure:{names.Current}";
    }
}
