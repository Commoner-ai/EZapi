using Il2Cpp;
using EZapi;

namespace EZapi.Sample;

/// <summary>Compile-ready example for an assembly-embedded PNG atlas.</summary>
public static class SpriteAssetExamples
{
    /// <summary>Declare this during mod initialization; it reads and validates resources but creates no Unity objects yet.</summary>
    public static EmbeddedSpriteAtlas CreateFieldKitAtlas()
        => SpriteAssets.CreateEmbeddedAtlas(
            ownerId: "ezapi.sample",
            atlasKey: "field-kit",
            assembly: typeof(SpriteAssetExamples).Assembly,
            definitions: new[]
            {
                new SpriteDefinition("field-kit", "EZapi.Sample.Textures.Items.field_kit.png", 1, 1)
                {
                    PivotX = 0.5f,
                    PivotY = 0.5f,
                    KeepReadable = false
                }
            },
            filter: SpriteFilter.Point);

    /// <summary>Call on the initialized game thread after the native sprite cache is ready.</summary>
    public static bool Install(EmbeddedSpriteAtlas atlas, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(log);
        return atlas.TryInstall(
            onInstalled: () => log("field-kit atlas installed"),
            onFailure: error => log("field-kit atlas failed: " + error.Message));
    }

    /// <summary>Assigns an existing live game item; its GridShape must be declared separately.</summary>
    public static void Assign(GameItem item, EmbeddedSpriteAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        atlas.Assign(item, "field-kit");
    }

    /// <summary>
    /// 在当前肖像字典就绪后调用；返回值可作为 ClientVisitApi/SupplierApi 的 spriteName。
    /// 此处用已有示例 PNG 演示绑定；正式访客应使用作者自己的肖像资源。
    /// </summary>
    public static string RegisterPortrait(EmbeddedSpriteAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        return atlas.RegisterPortrait("field-kit");
    }
}
