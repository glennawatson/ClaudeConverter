// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text;

namespace ClaudeNim.Aot.Optimizations;

/// <summary>Splits a shell command into the words a shell would see.</summary>
/// <remarks>
/// <para>
/// This matches Python's <c>shlex.split(command, posix=False)</c>, which is what the prompts being
/// answered were written against: words are separated by whitespace, quoted runs are kept whole,
/// and the quote characters stay in the word rather than being stripped.
/// </para>
/// <para>
/// An unbalanced quote is reported as a failure rather than guessed at. A command that cannot be
/// parsed is one the proxy should not be answering questions about.
/// </para>
/// </remarks>
public static class ShellWords
{
    /// <summary>Splits a command into words.</summary>
    /// <param name="command">The command to split.</param>
    /// <param name="words">The words the command splits into.</param>
    /// <returns><see langword="true"/> when the command parsed; <see langword="false"/> on an unbalanced quote.</returns>
    public static bool TrySplit(string command, out List<string> words)
    {
        words = [];

        if (string.IsNullOrEmpty(command))
        {
            return true;
        }

        var word = new StringBuilder();
        var quote = '\0';
        var started = false;

        foreach (var c in command)
        {
            if (quote != '\0')
            {
                _ = word.Append(c);
                quote = c == quote ? '\0' : quote;
                continue;
            }

            if (!char.IsWhiteSpace(c))
            {
                quote = c is '"' or '\'' ? c : quote;
                started = true;
                _ = word.Append(c);
                continue;
            }

            if (!started)
            {
                continue;
            }

            words.Add(word.ToString());
            _ = word.Clear();
            started = false;
        }

        return Finish(words, word, quote, started, out words);
    }

    /// <summary>Drops the leading <c>NAME=value</c> assignments a command may be prefixed with.</summary>
    /// <param name="words">The words the command split into.</param>
    /// <param name="assignments">How many leading assignments were dropped.</param>
    /// <returns>The words from the command itself onwards.</returns>
    public static List<string> StripEnvironmentAssignments(List<string> words, out int assignments)
    {
        ArgumentNullException.ThrowIfNull(words);

        var start = 0;
        while (start < words.Count && IsEnvironmentAssignment(words[start]))
        {
            start++;
        }

        assignments = start;
        return start == 0 ? words : words.GetRange(start, words.Count - start);
    }

    /// <summary>Determines whether a word is a shell-style environment assignment.</summary>
    /// <param name="word">The word to test.</param>
    /// <returns><see langword="true"/> when the word reads as <c>NAME=value</c>.</returns>
    public static bool IsEnvironmentAssignment(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return false;
        }

        var first = word[0];
        if (first != '_' && !char.IsAsciiLetter(first))
        {
            return false;
        }

        for (var i = 1; i < word.Length; i++)
        {
            var c = word[i];
            if (c == '=')
            {
                return true;
            }

            if (c != '_' && !char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>Closes the split, reporting an unbalanced quote as a failure.</summary>
    /// <param name="words">The words gathered so far.</param>
    /// <param name="pending">The word still being built.</param>
    /// <param name="quote">The quote character still open, or nul when none is.</param>
    /// <param name="started">Whether the pending word holds anything.</param>
    /// <param name="result">The words the command splits into.</param>
    /// <returns><see langword="true"/> when the command parsed.</returns>
    private static bool Finish(
        List<string> words,
        StringBuilder pending,
        char quote,
        bool started,
        out List<string> result)
    {
        if (quote != '\0')
        {
            result = [];
            return false;
        }

        if (started)
        {
            words.Add(pending.ToString());
        }

        result = words;
        return true;
    }
}
