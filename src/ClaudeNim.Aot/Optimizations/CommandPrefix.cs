// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
namespace ClaudeNim.Aot.Optimizations;

/// <summary>Works out the prefix a shell command should be permission-matched on.</summary>
/// <remarks>
/// <para>
/// Claude Code asks a model which prefix of a command its permission rules should be matched
/// against, so that allowing <c>git commit</c> does not also allow <c>git push</c>. The answer is
/// mechanical, so it is computed here rather than billed to the upstream.
/// </para>
/// <para>
/// This is a permission decision, so the failure modes are not symmetric. A command carrying a
/// substitution is reported as <see cref="InjectionDetected"/> rather than being summarised,
/// because the text after the substitution is not what will run. Anything else that cannot be
/// parsed returns <see cref="None"/>, which matches no rule and so falls back to asking the user.
/// </para>
/// </remarks>
public static class CommandPrefix
{
    /// <summary>The answer for a command whose real content is hidden behind a substitution.</summary>
    internal const string InjectionDetected = "command_injection_detected";

    /// <summary>The answer for a command no prefix could be taken from.</summary>
    internal const string None = "none";

    /// <summary>Commands whose first argument is part of the identity a rule matches on.</summary>
    /// <remarks>
    /// <c>git</c> alone is not a meaningful permission; <c>git push</c> is. For these the
    /// subcommand is carried into the prefix, provided it is a subcommand and not an option.
    /// </remarks>
    private static readonly string[] SubcommandDrivenCommands =
    [
        "git", "npm", "docker", "kubectl", "cargo", "go", "pip", "yarn",
    ];

    /// <summary>Extracts the prefix a command should be matched on.</summary>
    /// <param name="command">The command being asked about.</param>
    /// <returns>The prefix, or one of the sentinel answers.</returns>
    public static string Extract(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return None;
        }

        // A backtick or $( means the command that runs is not the command that was read.
        if (command.Contains('`', StringComparison.Ordinal)
            || command.Contains("$(", StringComparison.Ordinal))
        {
            return InjectionDetected;
        }

        return !ShellWords.TrySplit(command, out var words) || words.Count == 0
            ? None
            : FromWords(words);
    }

    /// <summary>Takes the prefix from the words a parsed command split into.</summary>
    /// <param name="words">The words of the command.</param>
    /// <returns>The prefix, or <see cref="None"/>.</returns>
    private static string FromWords(List<string> words)
    {
        var parts = ShellWords.StripEnvironmentAssignments(words, out var assignments);
        if (parts.Count == 0)
        {
            return None;
        }

        var head = parts[0];
        if (parts.Count > 1 && IsSubcommandDriven(head) && !parts[1].StartsWith('-'))
        {
            return $"{head} {parts[1]}";
        }

        return assignments == 0
            ? head
            : $"{string.Join(' ', words.GetRange(0, assignments))} {head}";
    }

    /// <summary>Determines whether a command's first argument belongs in its prefix.</summary>
    /// <param name="command">The command name.</param>
    /// <returns><see langword="true"/> when the subcommand is part of the identity.</returns>
    private static bool IsSubcommandDriven(string command)
    {
        foreach (var candidate in SubcommandDrivenCommands)
        {
            if (string.Equals(command, candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
