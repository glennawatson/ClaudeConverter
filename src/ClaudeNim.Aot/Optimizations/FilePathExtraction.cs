// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text;

namespace ClaudeNim.Aot.Optimizations;

/// <summary>Works out which files a shell command actually read.</summary>
/// <remarks>
/// <para>
/// Claude Code asks a model which files a command touched, so it can track what the session has
/// seen. The question is about reading, not listing: <c>ls</c> names files without showing their
/// contents, so it contributes nothing, while <c>cat</c> does.
/// </para>
/// <para>
/// A command that is neither recognised as listing nor as reading yields no paths. Claiming a
/// file was read when it was not would let the session believe it has context it never received,
/// which is worse than the extra round trip that omission costs.
/// </para>
/// </remarks>
public static class FilePathExtraction
{
    /// <summary>The answer for a command that read nothing.</summary>
    internal const string Empty = "<filepaths>\n</filepaths>";

    /// <summary>Commands that name files without revealing their contents.</summary>
    private static readonly string[] ListingCommands =
    [
        "ls", "dir", "find", "tree", "pwd", "cd", "mkdir", "rmdir", "rm",
    ];

    /// <summary>Commands whose arguments are files whose contents reach the transcript.</summary>
    private static readonly string[] ReadingCommands =
    [
        "cat", "head", "tail", "less", "more", "bat", "type",
    ];

    /// <summary>Options that consume the word after them, so it is not a path.</summary>
    private static readonly string[] GrepOptionsWithArguments = ["-e", "-f", "-m", "-A", "-B", "-C"];

    /// <summary>Options of the reading commands that consume the word after them.</summary>
    /// <remarks>
    /// <c>head -n 20 log.txt</c> reads one file, not two. Without this the count is taken for a
    /// path and the session is told it read a file called "20" — which is the precise failure this
    /// whole helper exists to avoid, so it is worth the short list.
    /// </remarks>
    private static readonly string[] ReadingOptionsWithArguments = ["-n", "-c", "--lines", "--bytes"];

    /// <summary>Options that supply grep's pattern, leaving every positional word a path.</summary>
    private static readonly string[] GrepPatternOptions = ["-e", "-f"];

    /// <summary>The separators a command name may be qualified by.</summary>
    private static readonly System.Buffers.SearchValues<char> PathSeparators =
        System.Buffers.SearchValues.Create(['/', '\\']);

    /// <summary>Extracts the paths a command read.</summary>
    /// <param name="command">The command being asked about.</param>
    /// <returns>The paths, in the element the prompt asks for.</returns>
    public static string Extract(string command)
    {
        if (string.IsNullOrWhiteSpace(command)
            || !ShellWords.TrySplit(command, out var words)
            || words.Count == 0)
        {
            return Empty;
        }

        var parts = ShellWords.StripEnvironmentAssignments(words, out _);
        if (parts.Count == 0)
        {
            return Empty;
        }

        var name = BaseName(parts[0]);

        if (Contains(ListingCommands, name))
        {
            return Empty;
        }

        if (Contains(ReadingCommands, name))
        {
            return Render(Operands(parts));
        }

        return string.Equals(name, "grep", StringComparison.Ordinal) ? Render(GrepPaths(parts)) : Empty;
    }

    /// <summary>Takes the command name out of a path it may have been invoked by.</summary>
    /// <param name="word">The word the command was invoked as.</param>
    /// <returns>The lower-cased command name.</returns>
    private static string BaseName(string word)
    {
        var cut = word.AsSpan().LastIndexOfAny(PathSeparators);
        return (cut < 0 ? word : word[(cut + 1)..]).ToLowerInvariant();
    }

    /// <summary>Collects the words of a command that are not options.</summary>
    /// <param name="parts">The words of the command, from its name onwards.</param>
    /// <returns>The operands.</returns>
    private static List<string> Operands(List<string> parts)
    {
        var paths = new List<string>(parts.Count - 1);
        var skip = false;

        for (var i = 1; i < parts.Count; i++)
        {
            var part = parts[i];

            if (skip)
            {
                skip = false;
                continue;
            }

            if (part.StartsWith('-'))
            {
                skip = Contains(ReadingOptionsWithArguments, part);
                continue;
            }

            paths.Add(part);
        }

        return paths;
    }

    /// <summary>Collects the paths a grep invocation searched.</summary>
    /// <param name="parts">The words of the command, from its name onwards.</param>
    /// <returns>The paths.</returns>
    /// <remarks>
    /// grep's first operand is its pattern rather than a path, unless the pattern was supplied by
    /// an option — in which case every operand is a path.
    /// </remarks>
    private static List<string> GrepPaths(List<string> parts)
    {
        var positional = new List<string>(parts.Count - 1);
        var patternFromOption = false;
        var skip = false;

        for (var i = 1; i < parts.Count; i++)
        {
            var part = parts[i];

            if (skip)
            {
                skip = false;
                continue;
            }

            if (part.StartsWith('-'))
            {
                if (Contains(GrepOptionsWithArguments, part))
                {
                    patternFromOption |= Contains(GrepPatternOptions, part);
                    skip = true;
                }

                continue;
            }

            positional.Add(part);
        }

        if (patternFromOption)
        {
            return positional;
        }

        return positional.Count > 1 ? positional.GetRange(1, positional.Count - 1) : [];
    }

    /// <summary>Renders the paths in the element the prompt asks for.</summary>
    /// <param name="paths">The paths that were read.</param>
    /// <returns>The rendered answer.</returns>
    private static string Render(List<string> paths)
    {
        if (paths.Count == 0)
        {
            return Empty;
        }

        var builder = new StringBuilder("<filepaths>\n");
        for (var i = 0; i < paths.Count; i++)
        {
            _ = builder.Append(paths[i]).Append('\n');
        }

        return builder.Append("</filepaths>").ToString();
    }

    /// <summary>Determines whether a word appears in a set.</summary>
    /// <param name="candidates">The set to look in.</param>
    /// <param name="word">The word to look for.</param>
    /// <returns><see langword="true"/> when the word is present.</returns>
    private static bool Contains(string[] candidates, string word)
    {
        foreach (var candidate in candidates)
        {
            if (string.Equals(candidate, word, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
