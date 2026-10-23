using System.Text.Json;
namespace EZapi;

/// <summary>One read-only refresh context. Re-resolve run data inside the callback; never cache native objects.</summary>
public sealed record FactionUiContext(GameRunIdentity Run, string LocaleCode);
/// <summary>Faction appearance. This registers presentation, not gameplay rules or reputation/save mutations.</summary>
public sealed record FactionUiDefinition(string Id, LocalizedItemText Name)
{
    public string Color { get; init; } = "#665867";
    public int Order { get; init; }
    public double Minimum { get; init; } = -200;
    public double Maximum { get; init; } = 200;
}
/// <summary>A benefit/penalty supplied by its owning Mod. Active is authoritative; the UI does not unlock perks.</summary>
public sealed record FactionPerkView(string Id, LocalizedItemText Name, LocalizedItemText Description, double Threshold, bool Active)
{
    public LocalizedItemText? Flavor { get; init; }
    /// <summary>Native icon recipe: star, happy, smile, sad, angry or skull.</summary>
    public string Icon { get; init; } = "smile";
    public bool AlwaysActive { get; init; }
}
/// <summary>Current faction state. Return null from the provider to hide a currently unknown faction.</summary>
public sealed record FactionReputationView(double Amount)
{
    public LocalizedItemText? Relationship { get; init; }
    public IReadOnlyList<FactionPerkView> Perks { get; init; } = Array.Empty<FactionPerkView>();
}
/// <summary>Resolved, immutable display state for native and registered factions.</summary>
public sealed record FactionUiSnapshot(string OwnerId, FactionUiDefinition Definition, FactionReputationView State, bool Native);

/// <summary>Bounded, strict JSON for one appearance definition; a C# provider supplies live gameplay state.</summary>
public static class FactionUiJson
{
    public static FactionUiDefinition Parse(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var output = new MemoryStream(); var buffer = new byte[4096]; int n;
        while ((n = source.Read(buffer, 0, buffer.Length)) > 0) {
            if (output.Length + n > 16384) throw new ArgumentException("EZAPI_FACTION_JSON: maximum 16 KiB.");
            output.Write(buffer, 0, n);
        }
        var bytes = output.ToArray(); ReadOnlyMemory<byte> json = bytes;
        if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191) json = bytes[3..];
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        Fields(doc.RootElement, "id name color order minimum maximum");
        var name = doc.RootElement.EnumerateObject().SingleOrDefault(p => p.Name.Equals("name", StringComparison.OrdinalIgnoreCase));
        if (name.Value.ValueKind != JsonValueKind.Object) throw new ArgumentException("EZAPI_FACTION_JSON: name required.");
        Fields(name.Value, "chinese english");
        var definition = JsonSerializer.Deserialize<FactionUiDefinition>(json.Span, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException("EZAPI_FACTION_JSON: definition required.");
        FactionUiRules.Validate(definition); return definition;
    }
    private static void Fields(JsonElement e, string fields)
    {
        if (e.ValueKind != JsonValueKind.Object) throw new ArgumentException("EZAPI_FACTION_JSON: object required.");
        var allowed = fields.Split(' '); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in e.EnumerateObject())
            if (!allowed.Contains(p.Name, StringComparer.OrdinalIgnoreCase) || !seen.Add(p.Name))
                throw new ArgumentException("EZAPI_FACTION_JSON: unknown or duplicate field " + p.Name);
    }
}

internal static class FactionUiRules
{
    internal static void Validate(FactionUiDefinition d)
    {
        if (d is null || !PresentationRules.Owner(d.Id) || d.Name is null ||
            !double.IsFinite(d.Minimum) || !double.IsFinite(d.Maximum) || d.Minimum >= 0 || d.Maximum <= 0 ||
            d.Minimum < -1000000 || d.Maximum > 1000000 || d.Color is null ||
            !System.Text.RegularExpressions.Regex.IsMatch(d.Color, @"\A#[0-9a-fA-F]{6}\z"))
            throw new ArgumentException("EZAPI_FACTION_DEFINITION: stable ID, #RRGGBB and range spanning zero required.");
        d.Name.Validate(160);
    }
    internal static FactionReputationView Copy(FactionReputationView state)
    {
        if (!double.IsFinite(state.Amount) || state.Perks is null || state.Perks.Count > 256)
            throw new ArgumentException("EZAPI_FACTION_STATE: finite reputation, at most 256 perks required.");
        state.Relationship?.Validate(160);
        var ids = new HashSet<string>(StringComparer.Ordinal); var perks = new List<FactionPerkView>();
        foreach (var p in state.Perks) {
            if (p is null || !PresentationRules.Owner(p.Id) || !ids.Add(p.Id) || !double.IsFinite(p.Threshold) || Math.Abs(p.Threshold)>1000000 ||
                !new[] { "star", "happy", "smile", "sad", "angry", "skull" }.Contains(p.Icon))
                throw new ArgumentException("EZAPI_FACTION_PERK: unique readable ID, finite threshold and native icon required.");
            p.Name.Validate(240); p.Description.Validate(4096); p.Flavor?.Validate(2048);
            perks.Add(p with { });
        }
        return state with { Perks = perks.AsReadOnly() };
    }
    internal static bool Matches(FactionUiSnapshot faction, string query, string locale)
    {
        var value = faction.Definition.Id + " " + faction.Definition.Name.ForLocale(locale);
        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(w => value.Contains(w, StringComparison.OrdinalIgnoreCase));
    }
    internal static double Position(double amount, double minimum, double maximum) => Math.Clamp((amount - minimum) / (maximum - minimum), 0, 1);
    // Match the original vertical ruler read from top to bottom. LINQ's stable sort
    // retains the owning Mod's order for several effects at the same threshold.
    internal static FactionPerkView[] OrderedPerks(IEnumerable<FactionPerkView> perks) => perks.OrderByDescending(p=>p.Threshold).ToArray();
    internal static (double Minimum,double Maximum) NativeRange(double amount,IEnumerable<FactionPerkView> perks)
    {
        double minimum=Math.Min(-200,amount),maximum=Math.Max(200,amount);
        foreach(var p in perks){minimum=Math.Min(minimum,p.Threshold);maximum=Math.Max(maximum,p.Threshold);}
        return (minimum,maximum);
    }
    internal sealed record Marker(double Threshold,IReadOnlyList<FactionPerkView> Perks);
    // Nearby markers share a hit target on a finite-width ruler. Keep every actual
    // threshold in the entries; switching an effect moves the detail tick exactly.
    internal static Marker[] Markers(IEnumerable<FactionPerkView> perks,double minimum=-200,double maximum=200)
    {
        var result=new List<Marker>();var entries=new List<FactionPerkView>();double start=0;
        foreach(var p in perks.OrderBy(p=>p.Threshold)){
            if(entries.Count>0&&(Position(p.Threshold,minimum,maximum)-Position(start,minimum,maximum))*1000>=42){result.Add(new(start,entries.ToArray()));entries.Clear();}
            if(entries.Count==0)start=p.Threshold;entries.Add(p);
        }
        if(entries.Count>0)result.Add(new(start,entries.ToArray()));return result.ToArray();
    }
    internal static double Progress(double amount, FactionPerkView perk) => perk.AlwaysActive || perk.Active ? 1 :
        perk.Threshold == 0 ? 0 : Math.Clamp(amount / perk.Threshold, 0, 1);
}

internal sealed class FactionUiCatalog
{
    internal sealed record Entry(string Owner, FactionUiDefinition Definition, Func<FactionUiContext, FactionReputationView?> Provider);
    internal readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, string> Errors = new(StringComparer.Ordinal);
    private bool _busy;
    internal PresentationResult Register(string owner, FactionUiDefinition definition, Func<FactionUiContext, FactionReputationView?> provider)
    {
        if (_busy) return new(PresentationStatus.Conflict, "EZAPI_FACTION_REENTRY");
        try {
            FactionUiRules.Validate(definition);
            if (!PresentationRules.Owned(owner, definition.Id) || provider is null) return new(PresentationStatus.Invalid,"EZAPI_FACTION_OWNER");
            if (Entries.ContainsKey(definition.Id) || Entries.Count >= 256) return new(PresentationStatus.Conflict,"EZAPI_FACTION_DUPLICATE_OR_LIMIT");
            Entries.Add(definition.Id, new(owner, definition with { }, provider)); return new(PresentationStatus.Queued);
        } catch (ArgumentException e) { return new(PresentationStatus.Invalid, e.Message); }
    }
    internal PresentationResult Remove(string owner, string id)
    {
        if (_busy) return new(PresentationStatus.Conflict,"EZAPI_FACTION_REENTRY");
        if (!PresentationRules.Owned(owner,id)) return new(PresentationStatus.Invalid,"EZAPI_FACTION_OWNER");
        if (Entries.TryGetValue(id,out var entry) && entry.Owner!=owner) return new(PresentationStatus.Conflict,"EZAPI_FACTION_OWNER");
        Entries.Remove(id); Errors.Remove(id); return new(PresentationStatus.Closed);
    }
    internal IReadOnlyList<FactionUiSnapshot> Read(FactionUiContext context, Func<bool> current)
    {
        var rows = new List<FactionUiSnapshot>(); if (_busy || !current()) return rows;
        _busy = true;
        try {
            foreach (var entry in Entries.Values.OrderBy(e=>e.Definition.Order).ThenBy(e=>e.Definition.Id,StringComparer.Ordinal)) {
                try {
                    var state=entry.Provider(context);
                    if (!current()) return Array.Empty<FactionUiSnapshot>();
                    if (state is not null) rows.Add(new(entry.Owner,entry.Definition,FactionUiRules.Copy(state),false));
                    Errors.Remove(entry.Definition.Id);
                } catch(Exception e) { Errors[entry.Definition.Id]="EZAPI_FACTION_PROVIDER: "+e.GetType().Name; }
                if (!current()) return Array.Empty<FactionUiSnapshot>();
            }
            return rows.AsReadOnly();
        } finally { _busy=false; }
    }
}
