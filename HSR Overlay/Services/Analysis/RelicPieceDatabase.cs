using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

public sealed class RelicPieceDatabase
{
    // setName -> slotKey -> list of piece names
    public Dictionary<string, Dictionary<string, List<string>>> Sets { get; private set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public static RelicPieceDatabase Load(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException("Relic piece database JSON not found", jsonPath);

        var json = File.ReadAllText(jsonPath);

        var sets = JsonSerializer.Deserialize<
            Dictionary<string, Dictionary<string, List<string>>>?
        >(json);

        if (sets == null)
            throw new Exception("Failed to deserialize relic piece database.");

        return new RelicPieceDatabase
        {
            Sets = new Dictionary<string, Dictionary<string, List<string>>>(
                sets, StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Try to determine (setName, slotKey) from a cleaned relic name.
    /// Returns null if no match found.
    /// </summary>
    public (string Set, RelicSlot Slot)? IdentifyRelicByName(string cleanedName)
    {
        cleanedName = NormalizeRelicName(cleanedName);

        foreach (var (setName, slotMap) in Sets)
        {
            foreach (var (slotKey, pieces) in slotMap)
            {
                foreach (var pieceName in pieces)
                {
                    var normPiece = NormalizeRelicName(pieceName);

                    if (cleanedName.Contains(normPiece, StringComparison.OrdinalIgnoreCase))
                    {
                        var slot = SlotKeyToEnum(slotKey);
                        return (setName, slot);
                    }
                }
            }
        }

        return null;
    }

    public static RelicSlot SlotKeyToEnum(string slotKey)
    {
        return slotKey.ToLowerInvariant() switch
        {
            "head" => RelicSlot.Head,
            "hands" => RelicSlot.Hands,
            "body" => RelicSlot.Body,
            "feet" => RelicSlot.Feet,
            "orb" => RelicSlot.Sphere,
            "rope" => RelicSlot.Rope,
            _ => RelicSlot.None
        };
    }

    private static string NormalizeRelicName(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        // unify fancy apostrophes into a normal ASCII '
        s = s
            .Replace('\u2019', '\'') // ’
            .Replace('\u2018', '\'') // ‘
            .Replace('\u02BC', '\''); // ʼ

        return s;
    }
}
