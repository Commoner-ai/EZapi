using System.Buffers.Binary;
using System.Reflection;
using EZapi;

var cases = new List<(string Name, Func<bool> Run)>();
void Add(string name, Func<bool> run) => cases.Add((name, run));
var assembly = Assembly.GetExecutingAssembly();
var valid = new SpriteDefinition("field-kit", "sprite-test.field-kit.png", 2, 1);

Add("embedded PNG is read and dimensions are checked", () => {
    var prepared = EmbeddedSpriteResources.Read(assembly, new[] { valid });
    var image = prepared.Single();
    var dimensions = PngAssetHeader.Read(image.Png, image.Definition.ResourceName);
    return image.Definition == valid && dimensions.Width == image.PixelWidth && dimensions.Height == image.PixelHeight && image.Png.Length >= 24;
});

Add("prepared declarations are independent of caller list mutation", () => {
    var definitions = new List<SpriteDefinition> { valid };
    var prepared = EmbeddedSpriteResources.Read(assembly, definitions);
    definitions.Clear();
    return prepared.Count == 1 && prepared[0].Definition.Key == "field-kit";
});

Add("duplicate keys reject the whole atlas", () => Throws<ArgumentException>(() =>
    EmbeddedSpriteResources.Read(assembly, new[] { valid, valid with { ResourceName = valid.ResourceName } })));

Add("missing embedded resource is reported", () => Throws<InvalidDataException>(() =>
    EmbeddedSpriteResources.Read(assembly, new[] { valid with { ResourceName = "missing.png" } })));

Add("invalid sprite key and grid bounds reject", () =>
    !EmbeddedSpriteResources.IsKey("../sprite") && !EmbeddedSpriteResources.IsKey("") &&
    Throws<ArgumentOutOfRangeException>(() => EmbeddedSpriteResources.Read(assembly, new[] { valid with { Width = 0 } })));

Add("non-finite pivot is rejected", () => Throws<ArgumentOutOfRangeException>(() =>
    EmbeddedSpriteResources.Read(assembly, new[] { valid with { PivotY = float.NaN } })));

Add("PNG signature corruption is rejected", () => {
    var bytes = ReadResource();
    bytes[0] = 0;
    return Throws<InvalidDataException>(() => PngAssetHeader.Read(bytes, "fixture"));
});

Add("zero or oversized PNG dimensions are rejected", () => {
    var zero = ReadResource();
    BinaryPrimitives.WriteInt32BigEndian(zero.AsSpan(16, 4), 0);
    var tooWide = ReadResource();
    BinaryPrimitives.WriteInt32BigEndian(tooWide.AsSpan(16, 4), 2049);
    return Throws<InvalidDataException>(() => PngAssetHeader.Read(zero, "zero")) &&
        Throws<InvalidDataException>(() => PngAssetHeader.Read(tooWide, "wide"));
});

Add("atlas limits and filter values are explicit", () =>
    EmbeddedSpriteResources.MaxSpriteCount == 128 && EmbeddedSpriteResources.MaxResourceBytes == 4 * 1024 * 1024 &&
    EmbeddedSpriteResources.MaxAtlasBytes == 16 * 1024 * 1024 && (int)SpriteFilter.Point == 0 && (int)SpriteFilter.Bilinear == 1);

var results = cases.Select(test => {
    try { return new { name = test.Name, passed = test.Run(), error = (string?)null }; }
    catch (Exception error) { return new { name = test.Name, passed = false, error = (string?)error.ToString() }; }
}).ToArray();
var passedCount = results.Count(result => result.passed);
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
    suite = "embedded-sprite-resources",
    passed = passedCount,
    failed = results.Length - passedCount,
    cases = results,
    productSources = new[] { "EmbeddedSpriteDefinition.cs" },
    scope = "resource lookup, bounded PNG inputs, identity/duplicate/pivot/dimension checks and defensive declaration snapshots; no Unity or native game mutation"
}));
if (passedCount != results.Length) Environment.Exit(1);

byte[] ReadResource()
{
    using var stream = assembly.GetManifestResourceStream("sprite-test.field-kit.png") ?? throw new InvalidOperationException("test image is missing");
    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return buffer.ToArray();
}

static bool Throws<T>(Action action) where T : Exception
{
    try { action(); return false; }
    catch (T) { return true; }
}
