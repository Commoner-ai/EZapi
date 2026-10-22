using System.Reflection;
using Il2Cpp;
using MelonLoader;

namespace EZapi;

/// <summary>Manual native smoke test for embedded resource loading, atlas install, and item assignment.</summary>
internal static class DebugSpriteAssets
{
    private static EmbeddedSpriteAtlas? _installedAtlas;
    private static EmbeddedSpriteAtlas? _stagedAtlas;
    private static bool _completed;
    internal static object? LastReport { get; private set; }

    internal static void Register()
    {
        var assembly = typeof(EZapiBootstrap).Assembly;
        var definition = new[] { new SpriteDefinition("terminal", "EZapi.Assets.manufacturing-terminal.png", 1, 1) };
        _installedAtlas = SpriteAssets.CreateEmbeddedAtlas("ezapi.debug", "asset-smoke", assembly, definition, SpriteFilter.Point);
        _stagedAtlas = SpriteAssets.CreateEmbeddedAtlas("ezapi.debug", "asset-staging-smoke", assembly, definition, SpriteFilter.Bilinear);
    }

    internal static void Clear()
    {
        _installedAtlas = null;
        _stagedAtlas = null;
        _completed = false;
        LastReport = null;
    }

    internal static string Run()
    {
        if (_completed) return "嵌入图集检查已完成；详情见 diagnostics.json。";
        if (!VisibleStoreInventory.IsReady || GameSession.Identity is null)
            return "请载入测试周目并进入店铺后检查。不会写入存档。";

        var checks = new List<object>();
        var passed = 0;
        var failed = 0;
        void Check(bool ok, string name)
        {
            checks.Add(new { name, passed = ok });
            if (ok) passed++; else failed++;
        }

        GameItem? normal = null;
        GameItem? staged = null;
        try
        {
            normal = ItemApi.Create("ezapi.debug.factory_item", false);
            staged = ItemApi.Create("ezapi.debug.factory_item", false);

            var callbackCount = 0;
            var installed = _installedAtlas!.TryInstall(() => callbackCount++, error => MelonLogger.Error("[EZapi] Sprite smoke install: " + error));
            Check(installed && _installedAtlas.Installed && _installedAtlas.Count == 1, "embedded PNG resource installs as one isolated atlas entry");
            var sprite = _installedAtlas.GetSprite("terminal");
            Check(sprite != null && sprite.texture != null && sprite.texture.width > 0 && sprite.texture.height > 0,
                "GetSprite returns a live decoded Unity sprite");
            _installedAtlas.Assign(normal, "terminal");
            Check(normal.spriteAtlasPath == _installedAtlas.NativeAtlasId && normal.spritePath == "terminal",
                "Assign uses native GameItem.SetSprite and reads atlas/sprite keys back");
            Check(_installedAtlas.TryInstall(() => callbackCount++), "repeated TryInstall is idempotent");
            Check(callbackCount == 1, "onInstalled fires only on the first install");

            var portraitKey = _installedAtlas.RegisterPortrait("terminal");
            var portrait = SpriteDict.Instance.GetSprite(portraitKey);
            Check(portrait != null && sprite != null && portrait.Pointer == sprite.Pointer,
                "portrait registration resolves the same Sprite through native SpriteDict.GetSprite");
            Check(_installedAtlas.RegisterPortrait("terminal") == portraitKey,
                "portrait registration in the current dictionary is idempotent");

            _stagedAtlas!.Assign(staged, "terminal", allowStaging: true);
            var stagedBeforeInstall = staged.spriteAtlasPath == _stagedAtlas.NativeAtlasId && staged.spritePath == "terminal";
            var stagedInstalled = _stagedAtlas.TryInstall();
            Check(stagedBeforeInstall && stagedInstalled && _stagedAtlas.Installed,
                "allowStaging assigns before atlas installation and resolves after installation");

            var duplicateRejected = false;
            try
            {
                SpriteAssets.CreateEmbeddedAtlas("ezapi.debug", "asset-smoke", typeof(EZapiBootstrap).Assembly,
                    new[] { new SpriteDefinition("terminal", "EZapi.Assets.manufacturing-terminal.png", 1, 1) });
            }
            catch (InvalidOperationException error) { duplicateRejected = error.Message.Contains("EZAPI_ASSET_DUPLICATE", StringComparison.Ordinal); }
            Check(duplicateRejected, "duplicate owner/atlas keys fail before native mutation");
            DebugGuideImages.Run(Check);
        }
        catch (Exception error)
        {
            Check(false, "execution: " + error);
            MelonLogger.Error("[EZapi] Embedded sprite smoke test failed: " + error);
        }
        finally
        {
            staged?.Destroy();
            normal?.Destroy();
        }

        _completed = true;
        LastReport = new
        {
            at = DateTimeOffset.Now,
            passed,
            failed,
            checks,
            atlas = new { installed = _installedAtlas?.Installed, failed = _installedAtlas?.Failed, count = _installedAtlas?.Count, id = _installedAtlas?.NativeAtlasId, error = _installedAtlas?.Failure },
            stagedAtlas = new { installed = _stagedAtlas?.Installed, failed = _stagedAtlas?.Failed, count = _stagedAtlas?.Count, id = _stagedAtlas?.NativeAtlasId, error = _stagedAtlas?.Failure },
            scope = "embedded resource preflight, Unity sprite decode, isolated atlas installation, native portrait registration/GetSprite readback, native SetSprite readback, staging, idempotence and duplicate rejection; temporary items destroyed; no inventory or save writes; portrait visual display and persistence not tested"
        };
        EZapiBootstrap.WriteDiagnostics();
        return $"内嵌图集：{passed}通过，{failed}失败；临时物品已销毁，未写入存档。";
    }
}
