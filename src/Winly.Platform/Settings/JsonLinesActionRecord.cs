using System.Text;
using System.Text.Json;
using Serilog;
using Winly.Core.Actions;

namespace Winly.Platform.Settings;

/// <summary>
/// The action record as JSON lines at <c>%LOCALAPPDATA%\Winly\actions.jsonl</c> (FR-025).
///
/// One object per line, because that makes an append an append: no read, no parse, no rewrite on
/// the path that runs while Winly is still speaking. A line truncated by a hard power-off costs
/// that one entry rather than the file.
///
/// Deliberately separate from the Serilog diagnostic log. This one is meant to be read by the
/// person using Winly (FR-024), and a diagnostics log is not readable by someone who did not write
/// the application — nor should clearing the record mean clearing some of the logs.
/// </summary>
public sealed class JsonLinesActionRecord : IActionRecord
{
    /// <summary>Roughly a month of heavy use, and well under a megabyte (FR-027).</summary>
    public const int MaxEntries = 500;

    private readonly string _path;
    private readonly Lock _gate = new();

    public JsonLinesActionRecord(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Winly",
            "actions.jsonl");

        // Trimmed once at startup rather than on every append, so the trim never lands in the
        // middle of a request. ponytail: a single session that runs 500+ actions stays oversized
        // until the next launch. Trim on append if that ever shows up.
        Trim();
    }

    public void Append(ActionRecordEntry entry)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, JsonSerializer.Serialize(entry) + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception failure)
        {
            // Failing to write the record is not a reason to fail the action it describes.
            Log.Warning(failure, "Could not append to the action record");
        }
    }

    public IReadOnlyList<ActionRecordEntry> Read()
    {
        try
        {
            lock (_gate)
            {
                if (!File.Exists(_path))
                {
                    return [];
                }

                return File.ReadAllLines(_path)
                    .Select(TryParse)
                    .OfType<ActionRecordEntry>()
                    .Reverse()
                    .ToArray();
            }
        }
        catch (Exception failure)
        {
            Log.Warning(failure, "Could not read the action record");
            return [];
        }
    }

    public void Clear()
    {
        try
        {
            lock (_gate)
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
        }
        catch (Exception failure)
        {
            Log.Warning(failure, "Could not clear the action record");
        }
    }

    private void Trim()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var lines = File.ReadAllLines(_path);
            if (lines.Length <= MaxEntries)
            {
                return;
            }

            File.WriteAllLines(_path, lines.Skip(lines.Length - MaxEntries), Encoding.UTF8);
        }
        catch (Exception failure)
        {
            Log.Warning(failure, "Could not trim the action record");
        }
    }

    /// <summary>A line written by an older version, or a half-line from a power cut, is skipped.</summary>
    private static ActionRecordEntry? TryParse(string line)
    {
        try
        {
            return line.Length == 0 ? null : JsonSerializer.Deserialize<ActionRecordEntry>(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
